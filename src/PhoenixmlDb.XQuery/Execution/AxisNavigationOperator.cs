using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Navigates from nodes to related nodes via an axis.
/// </summary>
public sealed class AxisNavigationOperator : PhysicalOperator
{
    public required PhysicalOperator Input { get; init; }
    public required Axis Axis { get; init; }
    public required NodeTest NodeTest { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Per XPath spec, all path expressions return unique nodes in document order.
        // We need to collect, deduplicate, and sort for axes that may produce
        // duplicates from multiple input nodes (e.g., @*/parent::* yields the same
        // parent from each attribute). Reverse axes are NOT included here — they
        // yield in axis order for correct predicate position(), then
        // DocumentOrderSortOperator re-sorts them after predicate evaluation.
        var needsSort = Axis is Axis.Child or Axis.Descendant or Axis.DescendantOrSelf
            or Axis.Following or Axis.FollowingSibling or Axis.Parent;

        if (needsSort)
        {
            var results = new List<XdmNode>();
            var seen = new HashSet<(ulong, NodeId)>();
            await foreach (var item in Input.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (item is not XdmNode node)
                {
                    if (item != null)
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0020",
                            $"An axis step ({Axis}::{NodeTest}) was used when the context item is not a node (got {DescribeItemType(item)})",
                            Location);
                    continue;
                }
                foreach (var related in NavigateAxis(node, context))
                {
                    if (MatchesNodeTest(related, context) && seen.Add(related.DocumentOrderKey))
                        results.Add(related);
                }
            }
            // Sort by NodeId for document order
            if (results.Count > 1)
                results.Sort(XdmNode.CompareDocumentOrder);
            foreach (var r in results)
                yield return r;
        }
        else
        {
            await foreach (var item in Input.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (item is not XdmNode node)
                {
                    if (item != null)
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0020",
                            $"An axis step ({Axis}::{NodeTest}) was used when the context item is not a node (got {DescribeItemType(item)})",
                            Location);
                    continue;
                }
                foreach (var related in NavigateAxis(node, context))
                {
                    if (MatchesNodeTest(related, context))
                        yield return related;
                }
            }
        }
    }

    private IEnumerable<XdmNode> NavigateAxis(XdmNode node, QueryExecutionContext context)
    {
        return Axis switch
        {
            Axis.Child => GetChildren(node, context),
            Axis.Descendant => GetDescendants(node, context, includeSelf: false, depth: 0),
            Axis.DescendantOrSelf => GetDescendants(node, context, includeSelf: true, depth: 0),
            Axis.Self => [node],
            Axis.Parent => GetParent(node, context),
            Axis.Ancestor => GetAncestors(node, context, includeSelf: false),
            Axis.AncestorOrSelf => GetAncestors(node, context, includeSelf: true),
            Axis.Attribute => GetAttributes(node, context),
            Axis.Following => GetFollowing(node, context),
            Axis.FollowingSibling => GetFollowingSiblings(node, context),
            Axis.Preceding => GetPreceding(node, context),
            Axis.PrecedingSibling => GetPrecedingSiblings(node, context),
            Axis.Namespace => GetNamespaceNodesStatic(node, context),
            _ => []
        };
    }

    internal static IEnumerable<XdmNode> GetNamespaceNodesStatic(XdmNode node, QueryExecutionContext context)
    {
        if (node is not XdmElement elem)
            yield break;

        // One namespace node per in-scope namespace binding (XDM §6.2). The binding set is
        // gathered by GatherInScopeNamespaces — the single source of truth shared with
        // fn:in-scope-prefixes so the two can never disagree — resolving ancestors through the
        // main node store (context.NodeStore), exactly as fn:in-scope-prefixes does.
        //
        // Each namespace node is stamped with a DISTINCT synthetic identity (the parent element's
        // TreeOrdinal plus a per-node id). Namespace nodes are not physically stored, so they have
        // no natural NodeId; giving them all NodeId.None makes their DocumentOrderKey identical,
        // and the document-order dedup applied after a path step (seen.Add(DocumentOrderKey)) would
        // then collapse every namespace node of an element into one — so element/namespace::* would
        // yield only a single node (e.g. just the default namespace) even though the element has
        // several in-scope namespaces. The synthetic ids are unique within the element and carry a
        // high marker bit so they cannot collide with a stored node's id.
        var nodeStore = context.NodeStore;
        ulong nsOrdinal = 0;
        foreach (var (prefix, ns) in GatherInScopeNamespaces(elem, id => nodeStore?.GetNode(id)))
        {
            yield return MakeNamespaceNode(elem, prefix, context.NamespaceResolver?.Invoke(ns) ?? "", ref nsOrdinal);
        }

        // Always include the xml namespace (implicitly in scope for every element).
        yield return MakeNamespaceNode(elem, "xml", "http://www.w3.org/XML/1998/namespace", ref nsOrdinal);
    }

    /// <summary>
    /// Builds one namespace node for <paramref name="elem"/> with a distinct synthetic identity so
    /// that a set of namespace nodes on the same element survive document-order deduplication (which
    /// keys on <c>(TreeOrdinal, NodeId)</c>). The parent element's <see cref="XdmNode.TreeOrdinal"/>
    /// groups the node with its tree; the per-element index (with a high marker bit) makes each id
    /// unique and out of range of any stored node id.
    /// </summary>
    private static XdmNamespace MakeNamespaceNode(XdmElement elem, string prefix, string uri, ref ulong index)
    {
        const ulong SyntheticNamespaceIdBit = 1UL << 62;
        var synthId = new NodeId(SyntheticNamespaceIdBit | ((elem.Id.Value & 0x3FFF_FFFF_FFFFUL) << 8) | (index & 0xFF));
        index++;
        return new XdmNamespace
        {
            Id = synthId,
            TreeOrdinal = elem.TreeOrdinal,
            Document = elem.Document,
            Parent = elem.Id,
            Prefix = prefix,
            Uri = uri
        };
    }

    /// <summary>
    /// Gathers the in-scope namespace bindings of <paramref name="elem"/> — its own declarations
    /// plus every binding inherited from an ancestor (XDM §6.2), excluding the implicit
    /// <c>xml</c> binding (callers add it). Bindings are yielded nearest-first, deduplicated by
    /// prefix (nearest wins), honouring <c>xmlns=""</c> default undeclarations and the
    /// copy-namespaces no-inherit marker. This is the single source of truth shared by the
    /// <c>namespace::</c> axis and <c>fn:in-scope-prefixes</c>, so the two can never disagree.
    /// </summary>
    /// <param name="elem">The element whose in-scope namespaces are wanted.</param>
    /// <param name="resolveParent">
    /// Resolves an ancestor by node id. Both callers pass the main node store
    /// (<c>context.NodeStore.GetNode</c>) so the axis and <c>fn:in-scope-prefixes</c> walk the same
    /// tree. Prefer this over a supplementary-provider-aware loader such as
    /// <c>QueryExecutionContext.LoadNode</c>: per-store <see cref="NodeId"/>s are small integers
    /// that collide across independently-parsed trees, so a loader that consults a pushed
    /// supplementary provider first could resolve an ancestor id to an unrelated node from another
    /// store and skew the walk.
    /// </param>
    internal static IEnumerable<(string Prefix, NamespaceId Namespace)> GatherInScopeNamespaces(
        XdmElement elem, Func<NodeId, XdmNode?>? resolveParent)
    {
        var seen = new HashSet<string>();
        var undeclared = new HashSet<string>();

        // Constructed elements (DocumentId 0): NamespaceDeclarations already hold the complete
        // in-scope set (explicit, name-prefix, and inherited bindings materialised at
        // construction). No ancestor walk — walking would double-count.
        if (elem.Document.Value == 0)
        {
            foreach (var nsDecl in elem.NamespaceDeclarations)
            {
                if (nsDecl.Prefix == ElementConstructorOperator.NoInheritMarkerPrefix)
                    continue;
                var prefix = nsDecl.Prefix ?? "";
                // xmlns="" undeclaration: don't surface a binding.
                if (string.IsNullOrEmpty(prefix) && nsDecl.Namespace == NamespaceId.None)
                    continue;
                if (!seen.Add(prefix))
                    continue;
                yield return (prefix, nsDecl.Namespace);
            }
            yield break;
        }

        // Parsed elements (non-zero DocumentId): the source records only the xmlns declarations
        // physically present on each element, so inherited bindings are collected by walking
        // ancestors (nearest wins).
        XdmNode? current = elem;
        while (current is not null)
        {
            bool stopAfterThis = false;
            if (current is XdmElement currentElem)
            {
                foreach (var nsDecl in currentElem.NamespaceDeclarations)
                {
                    if (nsDecl.Prefix == ElementConstructorOperator.NoInheritMarkerPrefix)
                    {
                        stopAfterThis = true;
                        continue;
                    }
                    var prefix = nsDecl.Prefix ?? "";
                    // xmlns="" undeclaration: the default namespace is out of scope from here up.
                    // Record it so no ancestor default leaks in, and surface no binding.
                    if (string.IsNullOrEmpty(prefix) && nsDecl.Namespace == NamespaceId.None)
                    {
                        undeclared.Add("");
                        seen.Add("");
                        continue;
                    }
                    if (undeclared.Contains(prefix) || !seen.Add(prefix))
                        continue;
                    yield return (prefix, nsDecl.Namespace);
                }
                // The element's own name prefix is in scope even if not physically declared on
                // the element (it is declared by some ancestor).
                if (!string.IsNullOrEmpty(currentElem.Prefix) && currentElem.Namespace != NamespaceId.None
                    && !undeclared.Contains(currentElem.Prefix) && seen.Add(currentElem.Prefix))
                {
                    yield return (currentElem.Prefix, currentElem.Namespace);
                }
            }
            if (stopAfterThis)
                break;
            current = current.Parent is { } pid && pid != NodeId.None ? resolveParent?.Invoke(pid) : null;
        }
    }

    private static IEnumerable<XdmNode> GetChildren(XdmNode node, QueryExecutionContext context)
    {
        if (node is XdmElement elem)
        {
            foreach (var childId in elem.Children)
            {
                var child = context.LoadNode(childId);
                if (child != null)
                    yield return child;
            }
        }
        else if (node is XdmDocument doc)
        {
            foreach (var childId in doc.Children)
            {
                var child = context.LoadNode(childId);
                if (child != null)
                    yield return child;
            }
        }
    }

    private static IEnumerable<XdmNode> GetDescendants(XdmNode node, QueryExecutionContext context, bool includeSelf, int depth)
    {
        context.CheckRecursionDepth(depth);

        if (includeSelf)
            yield return node;

        foreach (var child in GetChildren(node, context))
        {
            foreach (var desc in GetDescendants(child, context, includeSelf: true, depth: depth + 1))
            {
                yield return desc;
            }
        }
    }

    private static IEnumerable<XdmNode> GetParent(XdmNode node, QueryExecutionContext context)
    {
        if (node.Parent != NodeId.None)
        {
            var parent = context.LoadNode(node.Parent);
            if (parent != null)
                yield return parent;
        }
    }

    private static IEnumerable<XdmNode> GetAncestors(XdmNode node, QueryExecutionContext context, bool includeSelf)
    {
        if (includeSelf)
            yield return node;

        var current = node;
        while (current.Parent != NodeId.None)
        {
            var parent = context.LoadNode(current.Parent);
            if (parent == null)
                break;
            yield return parent;
            current = parent;
        }
    }

    private static IEnumerable<XdmNode> GetAttributes(XdmNode node, QueryExecutionContext context)
    {
        if (node is XdmElement elem)
        {
            foreach (var attrId in elem.Attributes)
            {
                var attr = context.LoadNode(attrId);
                if (attr != null)
                    yield return attr;
            }
        }
    }

    private static IEnumerable<XdmNode> GetFollowingSiblings(XdmNode node, QueryExecutionContext context)
        => GetFollowingSiblingsStatic(node, context);

    internal static IEnumerable<XdmNode> GetFollowingSiblingsStatic(XdmNode node, QueryExecutionContext context)
    {
        // Attribute and namespace nodes have no siblings
        if (node is XdmAttribute or XdmNamespace)
            yield break;

        if (node.Parent == NodeId.None)
            yield break;

        var parent = context.LoadNode(node.Parent);
        if (parent == null)
            yield break;

        var children = parent switch
        {
            XdmElement elem => elem.Children,
            XdmDocument doc => doc.Children,
            _ => (IReadOnlyList<NodeId>?)null
        };
        if (children == null)
            yield break;

        var found = false;
        foreach (var childId in children)
        {
            if (childId == node.Id)
            {
                found = true;
                continue;
            }
            if (found)
            {
                var sibling = context.LoadNode(childId);
                if (sibling != null)
                    yield return sibling;
            }
        }
    }

    private static IEnumerable<XdmNode> GetPrecedingSiblings(XdmNode node, QueryExecutionContext context)
        => GetPrecedingSiblingsStatic(node, context);

    internal static IEnumerable<XdmNode> GetPrecedingSiblingsStatic(XdmNode node, QueryExecutionContext context)
    {
        // Attribute and namespace nodes have no siblings
        if (node is XdmAttribute or XdmNamespace)
            yield break;

        if (node.Parent == NodeId.None)
            yield break;

        var parent = context.LoadNode(node.Parent);
        if (parent == null)
            yield break;

        var children = parent switch
        {
            XdmElement elem => elem.Children,
            XdmDocument doc => doc.Children,
            _ => (IReadOnlyList<NodeId>?)null
        };
        if (children == null)
            yield break;

        // Find the index of the context node, then iterate backwards (nearest first).
        // This is a reverse axis: position predicates count from the nearest sibling.
        int nodeIndex = -1;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] == node.Id)
            {
                nodeIndex = i;
                break;
            }
        }

        if (nodeIndex <= 0)
            yield break;

        for (int i = nodeIndex - 1; i >= 0; i--)
        {
            var sibling = context.LoadNode(children[i]);
            if (sibling != null)
                yield return sibling;
        }
    }

    private static IEnumerable<XdmNode> GetFollowing(XdmNode node, QueryExecutionContext context)
        => GetFollowingStatic(node, context);

    internal static IEnumerable<XdmNode> GetFollowingStatic(XdmNode node, QueryExecutionContext context)
    {
        // For attribute/namespace nodes, following axis navigates from the parent element.
        // The following nodes of an attribute are the same as the following nodes of
        // its parent element, plus the parent's children (which follow the attribute
        // in document order).
        if (node is XdmAttribute or XdmNamespace)
        {
            if (node.Parent != NodeId.None)
            {
                var parent = context.LoadNode(node.Parent);
                if (parent != null)
                {
                    // Children of the parent follow the attribute in document order
                    foreach (var child in GetChildren(parent, context))
                    {
                        foreach (var desc in GetDescendants(child, context, includeSelf: true, depth: 0))
                            yield return desc;
                    }
                    // Then following siblings of the parent and their descendants
                    foreach (var sibling in GetFollowingSiblings(parent, context))
                    {
                        foreach (var desc in GetDescendants(sibling, context, includeSelf: true, depth: 0))
                            yield return desc;
                    }
                    foreach (var ancestor in GetAncestors(parent, context, includeSelf: false))
                    {
                        foreach (var sibling in GetFollowingSiblings(ancestor, context))
                        {
                            foreach (var desc in GetDescendants(sibling, context, includeSelf: true, depth: 0))
                                yield return desc;
                        }
                    }
                }
            }
            yield break;
        }

        // Following axis: all nodes that are after this node in document order
        // but not descendants
        foreach (var sibling in GetFollowingSiblings(node, context))
        {
            foreach (var desc in GetDescendants(sibling, context, includeSelf: true, depth: 0))
                yield return desc;
        }

        foreach (var ancestor in GetAncestors(node, context, includeSelf: false))
        {
            foreach (var sibling in GetFollowingSiblings(ancestor, context))
            {
                foreach (var desc in GetDescendants(sibling, context, includeSelf: true, depth: 0))
                    yield return desc;
            }
        }
    }

    private static IEnumerable<XdmNode> GetPreceding(XdmNode node, QueryExecutionContext context)
        => GetPrecedingStatic(node, context);

    internal static IEnumerable<XdmNode> GetPrecedingStatic(XdmNode node, QueryExecutionContext context)
    {
        // For attribute/namespace nodes, preceding axis navigates from the parent element.
        // The preceding nodes of an attribute are the same as the preceding nodes of
        // its parent element (ancestors are still excluded).
        var effectiveNode = node;
        if (node is XdmAttribute or XdmNamespace)
        {
            if (node.Parent == NodeId.None)
                yield break;
            var parent = context.LoadNode(node.Parent);
            if (parent == null)
                yield break;
            effectiveNode = parent;
        }

        // Preceding axis: all nodes that are before this node in document order
        // but not ancestors
        foreach (var sibling in GetPrecedingSiblings(effectiveNode, context))
        {
            foreach (var desc in GetDescendants(sibling, context, includeSelf: true, depth: 0).Reverse())
                yield return desc;
        }

        foreach (var ancestor in GetAncestors(effectiveNode, context, includeSelf: false))
        {
            foreach (var sibling in GetPrecedingSiblings(ancestor, context))
            {
                foreach (var desc in GetDescendants(sibling, context, includeSelf: true, depth: 0).Reverse())
                    yield return desc;
            }
        }
    }

    private bool MatchesNodeTest(XdmNode node, QueryExecutionContext? context = null)
    {
        return NodeTest switch
        {
            NameTest nt => MatchesNameTest(node, nt, Axis, context),
            KindTest kt => MatchesKindTest(node, kt, context),
            _ => false
        };
    }

    internal static bool MatchesNameTest(XdmNode node, NameTest test, Axis axis, QueryExecutionContext? context = null)
    {
        // Per XPath spec: NameTest matches only nodes of the axis's principal node type.
        // attribute axis → attribute, namespace axis → namespace, all others → element.
        var matchesAttribute = axis == Axis.Attribute;
        var matchesNamespace = axis == Axis.Namespace;

        // Wildcard matches all nodes of the principal node type
        if (test.LocalName == "*")
        {
            if (test.NamespaceUri == null || test.NamespaceUri == "*")
            {
                if (matchesAttribute)
                    return node is XdmAttribute;
                if (matchesNamespace)
                    return node is XdmNamespace;
                return node is XdmElement;
            }
            // Q{}* on namespace axis — namespace nodes have no namespace URI,
            // so any namespace node matches the zero-length URI wildcard.
            if (matchesNamespace && test.NamespaceUri is { Length: 0 })
                return node is XdmNamespace;
            // ns:* — match specific namespace using resolved NamespaceId
            if (test.ResolvedNamespace.HasValue)
            {
                NamespaceId nodeNsW;
                if (matchesAttribute)
                    nodeNsW = node is XdmAttribute a ? a.Namespace : NamespaceId.None;
                else if (matchesNamespace)
                    return false; // Namespace nodes don't have namespaces
                else
                    nodeNsW = node is XdmElement e ? e.Namespace : NamespaceId.None;

                // NamespaceIds may collide between compiler and document store (they use
                // independent ID counters starting from the same base). Always verify via
                // URI string comparison when a NamespaceResolver is available.
#pragma warning disable CA1508 // NamespaceUri is nullable; analyzer false positive from branch pruning
                if (context?.NamespaceResolver != null && test.NamespaceUri != null)
#pragma warning restore CA1508
                {
                    if (nodeNsW == NamespaceId.None)
                        return string.IsNullOrEmpty(test.NamespaceUri);
                    var nodeUri = context.NamespaceResolver(nodeNsW);
                    return nodeUri != null && nodeUri == test.NamespaceUri;
                }
                return nodeNsW == test.ResolvedNamespace.Value;
            }
            return false;
        }

        // Named test: only matches principal node type
        var localName = (matchesAttribute, matchesNamespace) switch
        {
            (true, _) => node is XdmAttribute attr ? attr.LocalName : null,
            (_, true) => node is XdmNamespace ns ? ns.Prefix : null,
            _ => node is XdmElement elem ? elem.LocalName : null
        };

        if (localName != test.LocalName)
            return false;

        // *:name — any namespace matches
        if (test.NamespaceUri == "*")
            return true;

        // Get node's namespace for comparison
        var nodeNs = node switch
        {
            XdmElement elem => elem.Namespace,
            XdmAttribute attr => attr.Namespace,
            _ => NamespaceId.None
        };

        // Check namespace using resolved NamespaceId (set during static analysis).
        // NamespaceIds may collide between compiler and document store (they use
        // independent ID counters starting from the same base). Always verify via
        // URI string comparison when a NamespaceResolver is available.
        if (test.ResolvedNamespace.HasValue)
        {
            if (context?.NamespaceResolver != null && test.NamespaceUri != null)
            {
                if (nodeNs == NamespaceId.None)
                    return string.IsNullOrEmpty(test.NamespaceUri);
                var nodeUri = context.NamespaceResolver(nodeNs);
                return nodeUri != null && nodeUri == test.NamespaceUri;
            }
            return nodeNs == test.ResolvedNamespace.Value;
        }

        // No resolved namespace — unprefixed name matches only no-namespace nodes
        return nodeNs == NamespaceId.None;
    }

    /// <summary>
    /// Name test for KindTest contexts where the node kind is already validated.
    /// Does not filter by principal node type — matches elements and attributes.
    /// </summary>
    internal static bool MatchesNameForKindTest(XdmNode node, NameTest test, QueryExecutionContext? context = null)
    {
        // Wildcard: already kind-checked, so always matches
        if (test.LocalName == "*")
        {
            if (test.NamespaceUri == null || test.NamespaceUri == "*")
                return true;
            if (test.ResolvedNamespace.HasValue)
            {
                var ns = node switch
                {
                    XdmElement e => e.Namespace,
                    XdmAttribute a => a.Namespace,
                    _ => NamespaceId.None
                };
                return NamespacesMatch(ns, test, context);
            }
            return false;
        }

        var localName = node switch
        {
            XdmElement elem => elem.LocalName,
            XdmAttribute attr => attr.LocalName,
            XdmProcessingInstruction pi => pi.Target,
            _ => null
        };

        if (localName != test.LocalName)
            return false;
        if (test.NamespaceUri == "*")
            return true;

        var nodeNs = node switch
        {
            XdmElement elem => elem.Namespace,
            XdmAttribute attr => attr.Namespace,
            _ => NamespaceId.None
        };

        if (test.ResolvedNamespace.HasValue)
            return NamespacesMatch(nodeNs, test, context);

        // A prefixed kind-test name (element(x:foo)/attribute(x:foo)) resolved by the XSLT
        // namespace post-pass carries a NamespaceUri but no interned ResolvedNamespace
        // (unlike a path-step NameTest, whose ID is interned during static analysis). When a
        // NamespaceResolver is available, compare by URI string exactly as NamespacesMatch
        // does — otherwise the target namespace is silently ignored and the test matches
        // nothing. Martin Honnen 2026-07-30: self::attribute(x:expand-text).
        if (test.NamespaceUri != null && context?.NamespaceResolver != null)
            return NamespacesMatch(nodeNs, test, context);

        return nodeNs == NamespaceId.None;
    }

    /// <summary>
    /// Compare a node's NamespaceId against a NameTest's resolved namespace, preferring
    /// URI-string comparison when a NamespaceResolver is available. Mirrors the
    /// MatchesNameTest logic — analyzer-side and runtime-side ID allocators may issue
    /// different numeric IDs for the same URI (or the test's URI may never have been
    /// interned in the runtime store at all, as with EQName Q{...} in kind tests).
    /// </summary>
    private static bool NamespacesMatch(NamespaceId nodeNs, NameTest test, QueryExecutionContext? context)
    {
        if (context?.NamespaceResolver != null && test.NamespaceUri != null)
        {
            if (nodeNs == NamespaceId.None)
                return string.IsNullOrEmpty(test.NamespaceUri);
            var nodeUri = context.NamespaceResolver(nodeNs);
            return nodeUri != null && nodeUri == test.NamespaceUri;
        }
        return nodeNs == test.ResolvedNamespace!.Value;
    }

    internal static bool MatchesKindTest(XdmNode node, KindTest test, QueryExecutionContext? context = null)
    {
        var nodeKind = node switch
        {
            XdmElement => XdmNodeKind.Element,
            XdmAttribute => XdmNodeKind.Attribute,
            XdmText => XdmNodeKind.Text,
            XdmComment => XdmNodeKind.Comment,
            XdmProcessingInstruction => XdmNodeKind.ProcessingInstruction,
            XdmDocument => XdmNodeKind.Document,
            XdmNamespace => XdmNodeKind.Namespace,
            _ => unchecked((XdmNodeKind)(-1))
        };

        // node() matches any node (Kind == None means any-kind-test)
        if (test.Kind == XdmNodeKind.None)
            return true;

        if (nodeKind != test.Kind)
            return false;

        // If there's a name test, check it (kind already validated, no axis filtering)
        if (test.Name != null && !MatchesNameForKindTest(node, test.Name, context))
            return false;

        // If there's a type annotation test (e.g., attribute(foo, xs:integer)),
        // check the node's type annotation. Without schema processing,
        // elements have type xs:untyped and attributes have type xs:untypedAtomic.
        if (test.TypeName != null)
        {
            var typeName = test.TypeName.LocalName;
            // xs:untyped and xs:anyType match any element; xs:untypedAtomic matches any attribute
            if (nodeKind == XdmNodeKind.Element)
                return typeName is "untyped" or "anyType" or "xs:untyped" or "xs:anyType";
            if (nodeKind == XdmNodeKind.Attribute)
                return typeName is "untypedAtomic" or "anySimpleType" or "anyType"
                    or "xs:untypedAtomic" or "xs:anySimpleType" or "xs:anyType";
        }

        return true;
    }
}
