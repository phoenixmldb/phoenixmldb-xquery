using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Base class for physical operators in the execution plan.
/// </summary>
public abstract class PhysicalOperator
{
    /// <summary>
    /// Executes the operator and yields results.
    /// </summary>
    public abstract IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context);

    /// <summary>
    /// Estimated cost for this operator.
    /// </summary>
    public double EstimatedCost { get; init; }

    /// <summary>
    /// Estimated result cardinality.
    /// </summary>
    public long EstimatedCardinality { get; init; }
}

/// <summary>
/// Returns a constant value.
/// </summary>
public sealed class ConstantOperator : PhysicalOperator
{
    public required object? Value { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        yield return Value;
    }
}

/// <summary>
/// Returns an empty sequence.
/// </summary>
public sealed class EmptyOperator : PhysicalOperator
{
    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        yield break;
    }
}

/// <summary>
/// Returns the current context item.
/// </summary>
public sealed class ContextItemOperator : PhysicalOperator
{
    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        var item = context.ContextItem;
        if (item == null)
            throw new XQueryRuntimeException("XPDY0002", "The context item is absent");
        yield return item;
    }
}

/// <summary>
/// Returns a variable value.
/// </summary>
public sealed class VariableOperator : PhysicalOperator
{
    public required QName VariableName { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        var value = context.GetVariable(VariableName);
        // Explicitly handle sequences (object?[]) by enumerating items
        if (value is object?[] arr)
        {
            foreach (var item in arr)
                yield return item;
        }
        // XDM arrays (List<object?>) and maps (Dictionary) are single items — yield as-is
        else if (value is List<object?> or IDictionary<object, object?>)
        {
            yield return value;
        }
        else if (value is IEnumerable<object?> seq)
        {
            foreach (var item in seq)
                yield return item;
        }
        else if (value != null)
        {
            yield return value;
        }
    }
}

/// <summary>
/// Returns the document root for the context item's tree, or enumerates documents in a container.
/// </summary>
public sealed class DocumentRootOperator : PhysicalOperator
{
    public required ContainerId Container { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;

        // XPath semantics: "/" means the root of the context item's tree
        var contextItem = context.ContextItem;
        if (contextItem is XdmDocument doc)
        {
            yield return doc;
            yield break;
        }

        if (contextItem is XdmNode node)
        {
            // Navigate up to find the document root
            var current = node;
            while (current.Parent != NodeId.None)
            {
                var parent = context.LoadNode(current.Parent);
                if (parent == null)
                    break;
                current = parent;
            }
            // XPDY0050: If the root of the tree is not a document node, "/" is an error
            if (current is not XdmDocument)
                throw new XQueryRuntimeException("XPDY0050", "The context item for '/' is not in a tree rooted at a document node");
            yield return current;
            yield break;
        }

        // Context item is not a node — raise XPTY0020
        if (contextItem != null)
            throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0020", "An axis step was used when the context item is not a node");
        // No context item — yield nothing (XPDY0002 handled elsewhere)
    }
}

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
            var seen = new HashSet<NodeId>();
            await foreach (var item in Input.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (item is not XdmNode node)
                {
                    if (item != null)
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0020", "An axis step was used when the context item is not a node");
                    continue;
                }
                foreach (var related in NavigateAxis(node, context))
                {
                    if (MatchesNodeTest(related, context) && seen.Add(related.Id))
                        results.Add(related);
                }
            }
            // Sort by NodeId for document order
            if (results.Count > 1)
                results.Sort((a, b) => a.Id.CompareTo(b.Id));
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
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0020", "An axis step was used when the context item is not a node");
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
        if (node is XdmElement elem)
        {
            foreach (var nsDecl in elem.NamespaceDeclarations)
            {
                // Skip the no-inherit sentinel marker from copy-namespaces semantics.
                if (nsDecl.Prefix == ElementConstructorOperator.NoInheritMarkerPrefix)
                    continue;
                // Skip empty-uri default-namespace undeclaration (xmlns="").
                if (string.IsNullOrEmpty(nsDecl.Prefix) && nsDecl.Namespace == NamespaceId.None)
                    continue;
                var uri = context.NamespaceResolver?.Invoke(nsDecl.Namespace) ?? "";
                yield return new XdmNamespace
                {
                    Id = NodeId.None, // Synthetic node — not stored
                    Document = elem.Document,
                    Parent = elem.Id,
                    Prefix = nsDecl.Prefix,
                    Uri = uri
                };
            }
            // Always include the xml namespace (implicitly in scope for every element)
            yield return new XdmNamespace
            {
                Id = NodeId.None,
                Document = elem.Document,
                Parent = elem.Id,
                Prefix = "xml",
                Uri = "http://www.w3.org/XML/1998/namespace"
            };
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
            KindTest kt => MatchesKindTest(node, kt),
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
    internal static bool MatchesNameForKindTest(XdmNode node, NameTest test)
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
                return ns == test.ResolvedNamespace.Value;
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
            return nodeNs == test.ResolvedNamespace.Value;

        return nodeNs == NamespaceId.None;
    }

    internal static bool MatchesKindTest(XdmNode node, KindTest test)
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
        if (test.Name != null && !MatchesNameForKindTest(node, test.Name))
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

/// <summary>
/// Sorts nodes into document order (ascending NodeId) and removes duplicates.
/// Used after reverse-axis steps (ancestor, preceding, preceding-sibling) to ensure
/// the XPath spec requirement that path expressions return nodes in document order.
/// </summary>
public sealed class DocumentOrderSortOperator : PhysicalOperator
{
    public required PhysicalOperator Input { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var items = new List<object?>();
        var allNodes = true;
        var seen = new HashSet<NodeId>();
        await foreach (var item in Input.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (item is XdmNode node)
            {
                if (seen.Add(node.Id))
                    items.Add(node);
            }
            else
            {
                allNodes = false;
                items.Add(item);
            }
        }

        // XPTY0018: A path expression that returns a mix of nodes and non-nodes is a type error
        if (!allNodes && items.Any(i => i is XdmNode))
            throw new Functions.XQueryException("XPTY0018", "Path expression returns a mixture of nodes and non-node values");

        // Only sort when all results are nodes (document-order sorting).
        // When results contain atomics (e.g. path steps like //item/(@val+2)),
        // preserve evaluation order.
        if (allNodes && items.Count > 1)
        {
            items.Sort((a, b) => ((XdmNode)a!).Id.CompareTo(((XdmNode)b!).Id));
        }

        foreach (var item in items)
            yield return item;
    }
}

/// <summary>
/// Evaluates axis navigation + predicate filtering per input node, then
/// deduplicates and sorts the combined results.
/// This is necessary for XPath semantics: in a path like A//B[1], the predicate [1]
/// must apply per-parent (first B child of each node in A//), not to the flattened set.
/// </summary>
public sealed class PerNodeStepOperator : PhysicalOperator
{
    public required PhysicalOperator Input { get; init; }
    public required Axis Axis { get; init; }
    public required NodeTest NodeTest { get; init; }
    public required IReadOnlyList<PhysicalOperator> PredicateOperators { get; init; }
    public required IReadOnlyList<bool> PredicatePositional { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var results = new List<XdmNode>();
        var seen = new HashSet<NodeId>();

        await foreach (var item in Input.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (item is not XdmNode inputNode)
            {
                if (item != null)
                    throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0020", "An axis step was used when the context item is not a node");
                continue;
            }

            // Navigate axis from this single input node
            var stepResults = new List<XdmNode>();
            foreach (var related in NavigateAxis(inputNode, context))
            {
                if (MatchesNodeTest(related, context))
                    stepResults.Add(related);
            }

            // Apply predicates in sequence
            IReadOnlyList<object?> filtered = stepResults.Cast<object?>().ToList();
            for (var i = 0; i < PredicateOperators.Count; i++)
            {
                var predOp = PredicateOperators[i];
                var isPositional = PredicatePositional[i];
                var nextFiltered = new List<object?>();

                if (isPositional)
                {
                    var pos = 0;
                    foreach (var fi in filtered)
                    {
                        pos++;
                        context.PushContextItem(fi, pos, filtered.Count);
                        try
                        {
                            var result = await EvaluatePredicateAsync(predOp, context);
                            if (MatchesPredicate(result, pos))
                                nextFiltered.Add(fi);
                        }
                        finally
                        {
                            context.PopContextItem();
                        }
                    }
                }
                else
                {
                    var pos = 0;
                    foreach (var fi in filtered)
                    {
                        pos++;
                        context.PushContextItem(fi, pos);
                        try
                        {
                            var result = await EvaluatePredicateAsync(predOp, context);
                            if (MatchesPredicate(result, pos))
                                nextFiltered.Add(fi);
                        }
                        finally
                        {
                            context.PopContextItem();
                        }
                    }
                }

                filtered = nextFiltered;
            }

            // Add to combined results with deduplication
            foreach (var fi in filtered)
            {
                if (fi is XdmNode node && seen.Add(node.Id))
                    results.Add(node);
            }
        }

        // Sort combined results into document order
        if (results.Count > 1)
            results.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var r in results)
            yield return r;
    }

    private static bool MatchesPredicate(object? result, int position)
    {
        if (result is int intPos)
            return intPos == position;
        if (result is long longPos)
            return longPos == position;
        if (result is double dblPos)
            return !double.IsNaN(dblPos) && dblPos == Math.Floor(dblPos) && (long)dblPos == position;
        if (result is decimal decPos)
            return decPos == Math.Floor(decPos) && (long)decPos == position;
        return QueryExecutionContext.EffectiveBooleanValue(result);
    }

    private static async ValueTask<object?> EvaluatePredicateAsync(PhysicalOperator predOp, QueryExecutionContext context)
    {
        object? first = null;
        int count = 0;
        await foreach (var item in predOp.ExecuteAsync(context))
        {
            if (count == 0)
                first = item;
            count++;
            if (count > 1)
            {
                // Multiple items: if first is a node, result is the sequence (treated as true by EBV).
                // If first is not a node, FORG0006 per EBV rules for sequences of 2+ items.
                if (first is not Xdm.Nodes.XdmNode && first is not Xdm.TextNodeItem
                    && first is not System.Xml.XmlNode && first is not System.Xml.Linq.XNode)
                {
                    throw new XQueryRuntimeException("FORG0006",
                        "Effective boolean value not defined for a sequence of two or more items starting with a non-node value");
                }
                // First is a node with more items: treat as true (node sequence has EBV true)
                return true;
            }
        }
        return first;
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
            Axis.Namespace => AxisNavigationOperator.GetNamespaceNodesStatic(node, context),
            _ => []
        };
    }

    private bool MatchesNodeTest(XdmNode node, QueryExecutionContext? context = null) => NodeTest switch
    {
        NameTest nt => AxisNavigationOperator.MatchesNameTest(node, nt, Axis, context),
        KindTest kt => AxisNavigationOperator.MatchesKindTest(node, kt),
        _ => false
    };

    private static IEnumerable<XdmNode> GetChildren(XdmNode node, QueryExecutionContext context)
    {
        var children = node switch
        {
            XdmElement elem => elem.Children,
            XdmDocument doc => doc.Children,
            _ => null
        };
        if (children == null)
            yield break;
        foreach (var childId in children)
        {
            var child = context.LoadNode(childId);
            if (child != null)
                yield return child;
        }
    }

    private static IEnumerable<XdmNode> GetDescendants(XdmNode node, QueryExecutionContext context, bool includeSelf, int depth)
    {
        context.CheckRecursionDepth(depth);
        if (includeSelf)
            yield return node;
        foreach (var child in GetChildren(node, context))
            foreach (var desc in GetDescendants(child, context, includeSelf: true, depth: depth + 1))
                yield return desc;
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
            foreach (var attrId in elem.Attributes)
            {
                var attr = context.LoadNode(attrId);
                if (attr != null)
                    yield return attr;
            }
    }

    private static IEnumerable<XdmNode> GetFollowing(XdmNode node, QueryExecutionContext context)
    {
        return AxisNavigationOperator.GetFollowingStatic(node, context);
    }

    private static IEnumerable<XdmNode> GetFollowingSiblings(XdmNode node, QueryExecutionContext context)
    {
        return AxisNavigationOperator.GetFollowingSiblingsStatic(node, context);
    }

    private static IEnumerable<XdmNode> GetPreceding(XdmNode node, QueryExecutionContext context)
    {
        return AxisNavigationOperator.GetPrecedingStatic(node, context);
    }

    private static IEnumerable<XdmNode> GetPrecedingSiblings(XdmNode node, QueryExecutionContext context)
    {
        return AxisNavigationOperator.GetPrecedingSiblingsStatic(node, context);
    }
}

/// <summary>
/// Filters items by predicate.
/// </summary>
public sealed class FilterOperator : PhysicalOperator
{
    public required PhysicalOperator Input { get; init; }
    public required PhysicalOperator PredicateOperator { get; init; }

    /// <summary>
    /// Whether this filter uses positional predicates (position()/last()).
    /// When false, items can be streamed without full materialization.
    /// </summary>
    public bool RequiresPositionalAccess { get; init; } = true;

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        if (RequiresPositionalAccess)
        {
            // Must materialize for position/last functions
            var items = new List<object?>();
            await foreach (var item in Input.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                items.Add(item);
                context.CheckMaterializationLimit(items.Count);
            }

            var position = 0;
            foreach (var item in items)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                position++;
                context.PushContextItem(item, position, items.Count);
                try
                {
                    var result = await EvaluatePredicateAsync(context);

                    // Numeric predicates select by position
                    if (result is int intPos)
                    {
                        if (intPos == position)
                            yield return item;
                    }
                    else if (result is long longPos)
                    {
                        if (longPos == position)
                            yield return item;
                    }
                    else if (result is double dblPos)
                    {
                        if (!double.IsNaN(dblPos) && dblPos == Math.Floor(dblPos) && (long)dblPos == position)
                            yield return item;
                    }
                    else if (result is decimal decPos)
                    {
                        if (decPos == Math.Floor(decPos) && (long)decPos == position)
                            yield return item;
                    }
                    else if (QueryExecutionContext.EffectiveBooleanValue(result))
                    {
                        yield return item;
                    }
                }
                finally
                {
                    context.PopContextItem();
                }
            }
        }
        else
        {
            // Stream items without materialization — position/last not needed
            var position = 0;
            await foreach (var item in Input.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                position++;
                context.PushContextItem(item, position, -1);
                try
                {
                    var result = await EvaluatePredicateAsync(context);
                    if (result is int sIntPos)
                    {
                        if (sIntPos == position)
                            yield return item;
                    }
                    else if (result is long sLongPos)
                    {
                        if (sLongPos == position)
                            yield return item;
                    }
                    else if (result is double sDblPos)
                    {
                        if (!double.IsNaN(sDblPos) && sDblPos == Math.Floor(sDblPos) && (long)sDblPos == position)
                            yield return item;
                    }
                    else if (result is decimal sDecPos)
                    {
                        if (sDecPos == Math.Floor(sDecPos) && (long)sDecPos == position)
                            yield return item;
                    }
                    else if (QueryExecutionContext.EffectiveBooleanValue(result))
                    {
                        yield return item;
                    }
                }
                finally
                {
                    context.PopContextItem();
                }
            }
        }
    }

    private async ValueTask<object?> EvaluatePredicateAsync(QueryExecutionContext context)
    {
        // Execute the predicate operator and collect up to 2 items to detect multi-item sequences
        object? first = null;
        int count = 0;
        await foreach (var item in PredicateOperator.ExecuteAsync(context))
        {
            if (count == 0)
                first = item;
            count++;
            if (count > 1)
            {
                // Multiple items: if first is a node, result is the sequence (treated as true by EBV).
                // If first is not a node, FORG0006 per EBV rules for sequences of 2+ items.
                if (first is not Xdm.Nodes.XdmNode && first is not Xdm.TextNodeItem
                    && first is not System.Xml.XmlNode && first is not System.Xml.Linq.XNode)
                {
                    throw new XQueryRuntimeException("FORG0006",
                        "Effective boolean value not defined for a sequence of two or more items starting with a non-node value");
                }
                return true;
            }
        }
        return first;
    }
}

/// <summary>
/// Evaluates a FLWOR expression.
/// </summary>
public sealed class FlworOperator : PhysicalOperator
{
    public required IReadOnlyList<FlworClauseOperator> Clauses { get; init; }
    public required PhysicalOperator ReturnOperator { get; init; }
    /// <summary>XPath 4.0: otherwise expression — evaluated when FLWOR produces empty.</summary>
    public PhysicalOperator? OtherwiseOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Reset all count clause counters at the start of each FLWOR execution.
        // This ensures that when a FLWOR is re-entered (e.g., inner for inside outer for),
        // counters restart from 0.
        foreach (var c in Clauses)
        {
            if (c is CountClauseOperator cOp) cOp.ResetCounter();
        }

        var hasResults = false;
        await foreach (var tuple in ExecuteClausesAsync(context, 0))
        {
            context.PushScope();
            try
            {
                foreach (var (name, value) in tuple)
                {
                    context.BindVariable(name, value);
                }

                await foreach (var result in ReturnOperator.ExecuteAsync(context))
                {
                    hasResults = true;
                    yield return result;
                }
            }
            finally
            {
                context.PopScope();
            }
        }

        // XPath 4.0: if FLWOR produced nothing, evaluate otherwise
        if (!hasResults && OtherwiseOperator != null)
        {
            await foreach (var result in OtherwiseOperator.ExecuteAsync(context))
                yield return result;
        }
    }

    private async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteClausesAsync(
        QueryExecutionContext context, int index)
    {
        if (index >= Clauses.Count)
        {
            yield return new Dictionary<QName, object?>();
            yield break;
        }

        var clause = Clauses[index];

        // Barrier clauses (order by, group by) need ALL upstream tuples materialized first.
        // We split the clause chain: collect tuples from clauses [0..barrier-1] via recursive
        // materialization, apply the barrier, then continue with clauses [barrier+1..N].
        if (clause is OrderByClauseOperator orderBy)
        {
            // Caller already materialized tuples — this should not be reached directly.
            // But if it is, just pass through to next clause.
            await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
                yield return restTuple;
            yield break;
        }

        if (clause is GroupByClauseOperator groupBy)
        {
            // Caller already materialized tuples — this should not be reached directly.
            await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
                yield return restTuple;
            yield break;
        }

        // Find the next barrier clause (order by or group by) in the remaining clauses.
        // If found, we must materialize ALL tuples from clauses [index..barrier-1] before
        // applying the barrier operation.
        int? barrierIndex = null;
        for (int i = index + 1; i < Clauses.Count; i++)
        {
            if (Clauses[i] is OrderByClauseOperator or GroupByClauseOperator)
            {
                barrierIndex = i;
                break;
            }
        }

        if (barrierIndex.HasValue)
        {
            // Materialize all tuples from clauses [index..barrier-1]
            var allTuples = new List<Dictionary<QName, object?>>();
            await foreach (var tuple in MaterializeUpToAsync(context, index, barrierIndex.Value))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                allTuples.Add(tuple);
                context.CheckMaterializationLimit(allTuples.Count);
            }

            // Apply the barrier operation — and ALL remaining barriers, even when separated
            // by non-barrier clauses (let/where/count). Per XQuery semantics, once tuples are
            // materialized, let/where/count must be applied to the whole collection so that a
            // downstream `order by` or `group by` sees the full post-filter stream (QT3
            // use-case-groupby-Q6: group by → let → where → order by descending).
            int afterBarrierIndex = barrierIndex.Value;
            while (afterBarrierIndex < Clauses.Count)
            {
                var barrier = Clauses[afterBarrierIndex];
                if (barrier is OrderByClauseOperator orderByBarrier)
                {
                    allTuples = await SortTuplesAsync(allTuples, orderByBarrier, context);
                    afterBarrierIndex++;
                }
                else if (barrier is GroupByClauseOperator groupByBarrier)
                {
                    allTuples = await GroupTuplesAsync(allTuples, groupByBarrier, context);
                    afterBarrierIndex++;
                }
                else if (barrier is LetClauseOperator or WhereClauseOperator or CountClauseOperator)
                {
                    // Is there another barrier ahead? If not, bail out and let per-tuple
                    // processing handle the remaining suffix (preserves streaming).
                    bool hasDownstreamBarrier = false;
                    for (int j = afterBarrierIndex + 1; j < Clauses.Count; j++)
                    {
                        if (Clauses[j] is OrderByClauseOperator or GroupByClauseOperator)
                        { hasDownstreamBarrier = true; break; }
                    }
                    if (!hasDownstreamBarrier) break;

                    // Apply this non-barrier clause to every tuple in the collection.
                    var nextTuples = new List<Dictionary<QName, object?>>(allTuples.Count);
                    if (barrier is CountClauseOperator countOp) countOp.ResetCounter();
                    foreach (var t in allTuples)
                    {
                        context.PushScope();
                        foreach (var (n, v) in t) context.BindVariable(n, v);
                        try
                        {
                            if (barrier is LetClauseOperator letOp)
                            {
                                await foreach (var bindings in letOp.ExecuteAsync(context))
                                {
                                    var merged = new Dictionary<QName, object?>(t);
                                    foreach (var (n, v) in bindings) merged[n] = v;
                                    nextTuples.Add(merged);
                                }
                            }
                            else if (barrier is WhereClauseOperator whereOp)
                            {
                                bool pass = false;
                                await foreach (var _ in whereOp.ExecuteAsync(context)) { pass = true; break; }
                                if (pass) nextTuples.Add(t);
                            }
                            else if (barrier is CountClauseOperator cOp)
                            {
                                cOp.IncrementCounter();
                                var merged = new Dictionary<QName, object?>(t);
                                merged[cOp.Variable] = cOp.CurrentCount;
                                nextTuples.Add(merged);
                            }
                        }
                        finally { context.PopScope(); }
                    }
                    allTuples = nextTuples;
                    afterBarrierIndex++;
                }
                else
                {
                    break;
                }
            }

            // Continue with clauses after the last consecutive barrier
            foreach (var tuple in allTuples)
            {
                context.PushScope();
                foreach (var (name, value) in tuple)
                    context.BindVariable(name, value);

                try
                {
                    await foreach (var restTuple in ExecuteClausesAsync(context, afterBarrierIndex))
                    {
                        var merged = new Dictionary<QName, object?>(tuple);
                        foreach (var (name, value) in restTuple)
                            merged[name] = value;
                        yield return merged;
                    }
                }
                finally
                {
                    context.PopScope();
                }
            }
            yield break;
        }

        // Count clause: assigns a 1-based counter to each tuple from upstream.
        // The count maintains a running counter across all invocations within this FLWOR.
        if (clause is CountClauseOperator countClause)
        {
            countClause.IncrementCounter();
            context.BindVariable(countClause.Variable, countClause.CurrentCount);
            await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
            {
                // Only set in restTuple if a downstream clause hasn't already set this variable
                // (e.g., a second `count $index` re-assigning the same variable name).
                restTuple.TryAdd(countClause.Variable, countClause.CurrentCount);
                yield return restTuple;
            }
            yield break;
        }

        // No barrier ahead — normal streaming clause processing
        var allBindings = new List<Dictionary<QName, object?>>();
        await foreach (var bindings in clause.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            allBindings.Add(bindings);
            context.CheckMaterializationLimit(allBindings.Count);
        }

        foreach (var bindings in allBindings)
        {
            context.PushScope();
            foreach (var (name, value) in bindings)
                context.BindVariable(name, value);

            try
            {
                await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
                {
                    var merged = new Dictionary<QName, object?>(bindings);
                    foreach (var (name, value) in restTuple)
                        merged[name] = value;
                    yield return merged;
                }
            }
            finally
            {
                context.PopScope();
            }
        }
    }

    /// <summary>
    /// Materializes all tuples produced by clauses [startIndex..endIndex-1].
    /// Each tuple contains all variable bindings accumulated through the clause chain.
    /// </summary>
    private async IAsyncEnumerable<Dictionary<QName, object?>> MaterializeUpToAsync(
        QueryExecutionContext context, int startIndex, int endIndex)
    {
        if (startIndex >= endIndex)
        {
            yield return new Dictionary<QName, object?>();
            yield break;
        }

        var clause = Clauses[startIndex];
        var allBindings = new List<Dictionary<QName, object?>>();
        await foreach (var bindings in clause.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            allBindings.Add(bindings);
            context.CheckMaterializationLimit(allBindings.Count);
        }

        foreach (var bindings in allBindings)
        {
            context.PushScope();
            foreach (var (name, value) in bindings)
                context.BindVariable(name, value);

            try
            {
                await foreach (var rest in MaterializeUpToAsync(context, startIndex + 1, endIndex))
                {
                    var merged = new Dictionary<QName, object?>(bindings);
                    foreach (var (name, value) in rest)
                        merged[name] = value;
                    yield return merged;
                }
            }
            finally
            {
                context.PopScope();
            }
        }
    }

    private static async Task<List<Dictionary<QName, object?>>> SortTuplesAsync(
        List<Dictionary<QName, object?>> tuples,
        OrderByClauseOperator orderBy,
        QueryExecutionContext context)
    {
        var keyed = new List<(Dictionary<QName, object?> Tuple, List<object?> Keys)>();

        foreach (var tuple in tuples)
        {
            context.PushScope();
            foreach (var (name, value) in tuple)
                context.BindVariable(name, value);

            var keys = new List<object?>();
            foreach (var spec in orderBy.OrderSpecs)
            {
                object? key = null;
                int keyCount = 0;
                await foreach (var item in spec.KeyOperator.ExecuteAsync(context))
                {
                    keyCount++;
                    if (keyCount == 1) key = item;
                    else
                        throw new XQueryRuntimeException("XPTY0004",
                            "Order-by key expression must return a single value, got a sequence");
                }
                // Atomize sort keys — per XQuery spec, order by atomizes its key expressions.
                // For untyped elements, this produces xs:untypedAtomic which compares as string values.
                key = context.AtomizeWithNodes(key);
                keys.Add(key);
            }

            context.PopScope();
            keyed.Add((tuple, keys));
        }

        // Precompute StringComparison for each order spec (per-spec collation or default)
        var defaultComparison = context.DefaultCollation != null
            ? Functions.CollationHelper.GetStringComparison(context.DefaultCollation)
            : StringComparison.Ordinal;
        var specComparisons = orderBy.OrderSpecs.Select(s =>
            s.Collation != null
                ? Functions.CollationHelper.GetStringComparison(s.Collation)
                : defaultComparison).ToArray();

        // Use LINQ OrderBy for a stable sort — List<T>.Sort uses IntroSort which is
        // unstable and would violate the XQuery "stable order by" semantics that require
        // items with equal sort keys to preserve their original relative order.
        var sorted = keyed.OrderBy(x => x, Comparer<(Dictionary<QName, object?> Tuple, List<object?> Keys)>.Create(
            (a, b) =>
            {
                for (int i = 0; i < orderBy.OrderSpecs.Count; i++)
                {
                    var spec = orderBy.OrderSpecs[i];
                    var ka = i < a.Keys.Count ? a.Keys[i] : null;
                    var kb = i < b.Keys.Count ? b.Keys[i] : null;

                    var cmp = CompareValues(ka, kb, spec.EmptyOrder, specComparisons[i]);
                    if (spec.Direction == Ast.OrderDirection.Descending)
                        cmp = -cmp;

                    if (cmp != 0)
                        return cmp;
                }
                return 0;
            }));

        return sorted.Select(k => k.Tuple).ToList();
    }

    /// <summary>
    /// Groups tuples by grouping key variables. Non-key variables are aggregated into sequences.
    /// Per XQuery spec: grouping key variables retain a single value per group, while non-key
    /// variables accumulate all values from tuples in the group.
    /// </summary>
    private static async Task<List<Dictionary<QName, object?>>> GroupTuplesAsync(
        List<Dictionary<QName, object?>> tuples,
        GroupByClauseOperator groupBy,
        QueryExecutionContext context)
    {
        var groupKeyVarNames = new HashSet<QName>();
        foreach (var spec in groupBy.GroupingSpecs)
            groupKeyVarNames.Add(spec.Variable);

        // Per XQuery 3.0 spec, when successive grouping specs reference the same
        // variable name, only the LAST binding contributes to the composite key.
        // The "effective" key positions are the DISTINCT variable names, in the order of
        // their LAST occurrence among the specs. (group-013/016)
        var effectiveKeyVarNames = new List<QName>();
        var effectiveSpecIndex = new Dictionary<QName, int>();
        for (int si = 0; si < groupBy.GroupingSpecs.Count; si++)
        {
            var v = groupBy.GroupingSpecs[si].Variable;
            effectiveSpecIndex[v] = si;
        }
        // Preserve left-to-right order of last occurrences
        var seen = new HashSet<QName>();
        for (int si = groupBy.GroupingSpecs.Count - 1; si >= 0; si--)
        {
            var v = groupBy.GroupingSpecs[si].Variable;
            if (effectiveSpecIndex[v] == si && seen.Add(v))
                effectiveKeyVarNames.Insert(0, v);
        }

        // Build groups: key is a composite of all grouping variable values
        var groups = new List<(List<object?> KeyValues, List<Dictionary<QName, object?>> Tuples)>();

        foreach (var tuple in tuples)
        {
            // Compute grouping key values for this tuple — specs are evaluated left-to-right
            // and each rebind is visible to subsequent specs (within ONE scope push).
            var perVarKey = new Dictionary<QName, object?>();
            context.PushScope();
            foreach (var (name, value) in tuple)
                context.BindVariable(name, value);
            foreach (var spec in groupBy.GroupingSpecs)
            {
                object? keyVal;
                if (spec.KeyOperator != null)
                {
                    // Explicit key expression: group by $var := expr — evaluated in the
                    // current scope (which already reflects any earlier spec rebindings).
                    keyVal = null;
                    await foreach (var item in spec.KeyOperator.ExecuteAsync(context))
                    {
                        keyVal = item;
                        break;
                    }
                }
                else
                {
                    // Implicit: group by $var — uses the variable's current binding.
                    // Per XQuery 3.0 spec, the variable MUST be bound in the tuple stream of
                    // the enclosing FLWOR (i.e. introduced by for/let/window/count inside it).
                    if (!tuple.ContainsKey(spec.Variable) && !perVarKey.ContainsKey(spec.Variable))
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQST0094",
                            $"Grouping variable ${spec.Variable.LocalName} is not bound by a for/let/window/count clause of the enclosing FLWOR");
                    keyVal = perVarKey.TryGetValue(spec.Variable, out var cur) ? cur : tuple[spec.Variable];
                }
                // When a type declaration is present we must preserve xs:untypedAtomic so
                // that the type check below fails with XPTY0004 rather than silently coercing.
                keyVal = spec.TypeDeclaration != null
                    ? QueryExecutionContext.AtomizeTyped(keyVal)
                    : context.AtomizeWithNodes(keyVal);
                // Per XQuery spec: the grouping key value must be zero or one atomic item.
                // If atomization produces a sequence of more than one item, raise XPTY0004.
                if (keyVal is System.Collections.IEnumerable en && keyVal is not string && keyVal is not byte[])
                {
                    var items = new List<object?>();
                    foreach (var it in en) items.Add(it);
                    if (items.Count > 1)
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0004",
                            "Grouping key must be zero or one atomic value; got sequence of " + items.Count);
                    keyVal = items.Count == 1 ? items[0] : null;
                }
                // Normalize dateTime/date/time keys to UTC so values that differ only in
                // timezone representation compare as equal (group-019).
                keyVal = NormalizeGroupingKey(keyVal);

                // Type declaration check (group by $var as T := ...).
                // Per XQuery 3.0, the declared type applies to the POST-ATOMIZED key. The type
                // must be an atomic SequenceType; non-atomic declared types (e.g. attribute(*))
                // can never match atomized values and raise XPTY0004. Similarly, xs:string does
                // not accept xs:untypedAtomic without explicit cast ⇒ XPTY0004.
                if (spec.TypeDeclaration != null)
                {
                    var td = spec.TypeDeclaration;
                    bool isAtomicTarget = td.ItemType is not (
                        Ast.ItemType.Item or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                        or Ast.ItemType.ProcessingInstruction or Ast.ItemType.Function
                        or Ast.ItemType.Map or Ast.ItemType.Array);
                    if (!isAtomicTarget)
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0004",
                            $"Grouping key type {td.ItemType} is not an atomic type");
                    if (keyVal != null)
                    {
                        // Atomized untypedAtomic does NOT implicitly convert to other atomic types
                        // in group-by type declarations (per spec: no implicit cast).
                        if (keyVal is Xdm.XsUntypedAtomic && td.ItemType != Ast.ItemType.UntypedAtomic && td.ItemType != Ast.ItemType.AnyAtomicType)
                            throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0004",
                                $"Grouping key has type xs:untypedAtomic but declared type is {td.ItemType}");
                        TypeCastHelper.RequireAtomicTypeMatch(keyVal, td.ItemType, $"group by ${spec.Variable.LocalName}");
                    }
                }

                perVarKey[spec.Variable] = keyVal;
                // Rebind variable in context so subsequent specs see this value.
                context.BindVariable(spec.Variable, keyVal);
            }
            context.PopScope();

            // Assemble the effective composite key in the order of distinct variable names.
            var keyValues = new List<object?>();
            foreach (var vn in effectiveKeyVarNames)
                keyValues.Add(perVarKey[vn]);

            // For collation, build a list of the LAST spec for each effective var.
            var effectiveSpecs = effectiveKeyVarNames
                .Select(vn => groupBy.GroupingSpecs[effectiveSpecIndex[vn]]).ToList();

            // Find existing group with matching key
            var found = false;
            foreach (var group in groups)
            {
                if (GroupKeysEqual(group.KeyValues, keyValues, effectiveSpecs, context.DefaultCollation))
                {
                    group.Tuples.Add(tuple);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                groups.Add((keyValues, new List<Dictionary<QName, object?>> { tuple }));
            }
        }

        // Build result tuples: one per group
        var result = new List<Dictionary<QName, object?>>();
        foreach (var (keyValues, groupTuples) in groups)
        {
            var resultTuple = new Dictionary<QName, object?>();

            // Set grouping key variables to the single key value (only distinct var names).
            for (int i = 0; i < effectiveKeyVarNames.Count; i++)
            {
                resultTuple[effectiveKeyVarNames[i]] = keyValues[i];
            }

            // Aggregate non-key variables into sequences
            var allVarNames = new HashSet<QName>();
            foreach (var t in groupTuples)
                foreach (var name in t.Keys)
                    allVarNames.Add(name);

            foreach (var varName in allVarNames)
            {
                if (groupKeyVarNames.Contains(varName))
                    continue;

                var values = new List<object?>();
                foreach (var t in groupTuples)
                {
                    if (t.TryGetValue(varName, out var val))
                        values.Add(val);
                }

                resultTuple[varName] = values.Count switch
                {
                    0 => null,
                    1 => values[0],
                    _ => values.ToArray()
                };
            }

            result.Add(resultTuple);
        }

        return result;
    }

    /// <summary>
    /// Normalizes a grouping key so that equivalent values compare equal.
    /// Notably: xs:dateTime/date/time values with timezone are normalized to UTC
    /// so that two dateTimes referring to the same instant in different offsets
    /// fall into the same group (per QT3 test group-019).
    /// </summary>
    private static object? NormalizeGroupingKey(object? key)
    {
        if (key is PhoenixmlDb.Xdm.XsDateTime xdt)
        {
            // For grouping-key equality, compare on the absolute instant so that values
            // differing only in timezone representation collapse into the same group.
            // Per XQuery spec, a value without a timezone is treated as being in implicit
            // timezone (local) for comparison. Our parser stores no-tz values with offset 0
            // (UTC) so we must re-interpret the clock values as local before converting.
            DateTimeOffset dto = xdt.Value;
            if (!xdt.HasTimezone)
            {
                var local = DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Unspecified);
                var offset = TimeZoneInfo.Local.GetUtcOffset(local);
                dto = new DateTimeOffset(local, offset);
            }
            var utc = dto.ToUniversalTime();
            return new PhoenixmlDb.Xdm.XsDateTime(utc, true) { ExtendedYear = xdt.ExtendedYear };
        }
        return key;
    }

    private static bool GroupKeysEqual(List<object?> a, List<object?> b, IReadOnlyList<GroupingSpecOperator>? specs = null, string? defaultCollation = null)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] == null && b[i] == null) continue;
            if (a[i] == null || b[i] == null) return false;
            // Apply collation for string-typed grouping keys.
            // Per XQuery 3.0 §3.12.7: if no explicit collation is specified on the grouping
            // spec, the default collation from the static context applies.
            var coll = specs != null && i < specs.Count ? specs[i].Collation : null;
            coll ??= defaultCollation;
            if (coll != null && (a[i] is string || a[i] is Xdm.XsUntypedAtomic) && (b[i] is string || b[i] is Xdm.XsUntypedAtomic))
            {
                var sa = a[i]!.ToString() ?? "";
                var sb = b[i]!.ToString() ?? "";
                var cmp = Functions.CollationHelper.GetStringComparison(coll);
                if (!string.Equals(sa, sb, cmp)) return false;
                continue;
            }
            // Fall back to the shared XQuery value comparer so group-by keys see the same
            // equality semantics as distinct-values (numeric promotion, tz-aware date/time,
            // gYear-family implicit-tz handling).
            if (!Functions.XQueryValueComparer.Instance.Equals(a[i], b[i])) return false;
        }
        return true;
    }

    private static int CompareValues(object? a, object? b, Ast.EmptyOrder emptyOrder,
        StringComparison stringComparison = StringComparison.Ordinal)
    {
        // Treat NaN as empty per XQuery spec — NaN follows empty order policy
        bool aNaN = a is double da && double.IsNaN(da) || a is float fa && float.IsNaN(fa);
        bool bNaN = b is double db && double.IsNaN(db) || b is float fb && float.IsNaN(fb);
        if (aNaN) a = null;
        if (bNaN) b = null;

        if (a == null && b == null)
            return 0;
        if (a == null)
            return emptyOrder == Ast.EmptyOrder.Least ? -1 : 1;
        if (b == null)
            return emptyOrder == Ast.EmptyOrder.Least ? 1 : -1;

        // Unwrap XsTypedString to plain string
        if (a is Xdm.XsTypedString tsA2) a = tsA2.Value;
        if (b is Xdm.XsTypedString tsB2) b = tsB2.Value;

        // XPTY0004: incomparable types in order-by (e.g., xs:string vs xs:integer / xs:date)
        bool aIsStr = a is string or Xdm.XsUntypedAtomic;
        bool bIsStr = b is string or Xdm.XsUntypedAtomic;
        bool aIsNum = a is long or int or short or byte or double or float or decimal;
        bool bIsNum = b is long or int or short or byte or double or float or decimal;
        bool aIsDate = a is Xdm.XsDate or Xdm.XsDateTime or Xdm.XsTime;
        bool bIsDate = b is Xdm.XsDate or Xdm.XsDateTime or Xdm.XsTime;
        if ((aIsStr && (bIsNum || bIsDate)) || (bIsStr && (aIsNum || aIsDate))
            || (aIsNum && bIsDate) || (bIsNum && aIsDate))
            throw new XQueryRuntimeException("XPTY0004",
                $"order-by keys have incomparable types: {a.GetType().Name} vs {b.GetType().Name}");

        // String comparisons use the per-spec collation or the default collation.
        // Use CollationHelper.CompareStrings for correct Unicode codepoint ordering.
        if (a is string sa && b is string sb)
            return Functions.CollationHelper.CompareStrings(sa, sb, stringComparison);
        if (a is Xdm.XsUntypedAtomic && b is Xdm.XsUntypedAtomic)
            return Functions.CollationHelper.CompareStrings(a.ToString()!, b.ToString()!, stringComparison);
        if ((a is string || a is Xdm.XsUntypedAtomic) && (b is string || b is Xdm.XsUntypedAtomic))
            return Functions.CollationHelper.CompareStrings(a.ToString()!, b.ToString()!, stringComparison);

        // Binary comparison: octet-by-octet unsigned byte ordering for same binary type.
        if (a is Xdm.XdmValue abv && abv.RawValue is byte[] aBytes)
        {
            if (b is Xdm.XdmValue bbv && bbv.RawValue is byte[] bBytes && abv.Type == bbv.Type)
                return aBytes.AsSpan().SequenceCompareTo(bBytes);
            throw new XQueryRuntimeException("XPTY0004",
                $"order-by keys have incomparable types: {abv.Type} vs {b.GetType().Name}");
        }
        if (b is Xdm.XdmValue bbv2 && bbv2.RawValue is byte[])
            throw new XQueryRuntimeException("XPTY0004",
                $"order-by keys have incomparable types: {a.GetType().Name} vs {bbv2.Type}");

        if (a is IComparable ca)
        {
            try
            { return ca.CompareTo(b); }
            catch (ArgumentException)
            { return string.Compare(a?.ToString(), b?.ToString(), StringComparison.Ordinal); }
        }

        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>
/// Base for FLWOR clause operators.
/// </summary>
public abstract class FlworClauseOperator
{
    public abstract IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context);
}

/// <summary>
/// For clause operator.
/// </summary>
public sealed class ForClauseOperator : FlworClauseOperator
{
    public required IReadOnlyList<ForBindingOperator> Bindings { get; init; }

    private static bool IsAtomicForTarget(Ast.ItemType t) => t is not (
        Ast.ItemType.Item or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
        or Ast.ItemType.ProcessingInstruction or Ast.ItemType.Function
        or Ast.ItemType.Map or Ast.ItemType.Array);
    /// <summary>True for "for member" (XPath 4.0) — iterates over array members.</summary>
    public bool IsMember { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        await foreach (var tuple in ExecuteBindingsAsync(context, 0))
        {
            yield return tuple;
        }
    }

    private async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteBindingsAsync(
        QueryExecutionContext context, int index)
    {
        if (index >= Bindings.Count)
        {
            yield return new Dictionary<QName, object?>();
            yield break;
        }

        var binding = Bindings[index];
        var position = 0;

        // XPath 4.0: "for member" iterates over array members
        if (IsMember)
        {
            // Collect the input (should be a single array value)
            var itemCount = 0;
            object? inputResult = null;
            await foreach (var item in binding.InputOperator.ExecuteAsync(context))
            {
                itemCount++;
                inputResult = item;
            }
            if (itemCount > 1)
                throw new XQueryRuntimeException("XPTY0004", "for member requires a single array value, not a sequence of multiple items");

            IList<object?> members;
            if (inputResult is List<object?> list) members = list;
            else if (inputResult is object?[] arr) members = arr;
            else members = inputResult != null ? new[] { inputResult } : System.Array.Empty<object?>();

            foreach (var member in members)
            {
                position++;
                var tuple = new Dictionary<QName, object?> { [binding.Variable] = member };
                if (binding.PositionalVariable.HasValue)
                    tuple[binding.PositionalVariable.Value] = (long)position;

                context.PushScope();
                foreach (var kvp in tuple)
                    context.BindVariable(kvp.Key, kvp.Value);

                await foreach (var innerTuple in ExecuteBindingsAsync(context, index + 1))
                {
                    var merged = new Dictionary<QName, object?>(tuple);
                    foreach (var kvp in innerTuple) merged[kvp.Key] = kvp.Value;
                    yield return merged;
                }
                context.PopScope();
            }
            yield break;
        }

        // Stream when no positional variable is needed; otherwise materialize
        if (!binding.PositionalVariable.HasValue && !binding.AllowingEmpty)
        {
            await foreach (var rawItem in binding.InputOperator.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                position++;
                var item = rawItem;
                if (binding.TypeDeclaration != null)
                {
                    // XQuery §3.8.1: for clause type declaration — strict SequenceType matching
                    // (no promotion, no untypedAtomic casting)
                    if (item != null && !TypeCastHelper.MatchesSequenceItemType(item, binding.TypeDeclaration))
                        throw new XQueryRuntimeException("XPTY0004",
                            $"for ${binding.Variable.LocalName}: value does not match declared type {binding.TypeDeclaration.ItemType}");
                }
                var tuple = new Dictionary<QName, object?> { [binding.Variable] = item };

                context.PushScope();
                context.BindVariable(binding.Variable, item);

                try
                {
                    await foreach (var rest in ExecuteBindingsAsync(context, index + 1))
                    {
                        var merged = new Dictionary<QName, object?>(tuple);
                        foreach (var (name, value) in rest)
                            merged[name] = value;
                        yield return merged;
                    }
                }
                finally
                {
                    context.PopScope();
                }
            }
            yield break;
        }

        var items = new List<object?>();
        await foreach (var item in binding.InputOperator.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            items.Add(item);
            context.CheckMaterializationLimit(items.Count);
        }

        if (items.Count == 0 && binding.AllowingEmpty)
        {
            // Per XQuery 3.1 §3.12.4.1: if allowing empty with a type declaration
            // that requires exactly-one or one-or-more, empty binding is a type error
            if (binding.TypeDeclaration != null
                && binding.TypeDeclaration.Occurrence is Ast.Occurrence.ExactlyOne or Ast.Occurrence.OneOrMore)
            {
                throw new XQueryRuntimeException("XPTY0004",
                    $"Variable ${binding.Variable.LocalName} declared as {binding.TypeDeclaration} " +
                    $"but 'allowing empty' produced an empty sequence");
            }
            var tuple = new Dictionary<QName, object?> { [binding.Variable] = null };
            if (binding.PositionalVariable.HasValue)
            {
                tuple[binding.PositionalVariable.Value] = 0;
            }

            await foreach (var rest in ExecuteBindingsAsync(context, index + 1))
            {
                var merged = new Dictionary<QName, object?>(tuple);
                foreach (var (name, value) in rest)
                    merged[name] = value;
                yield return merged;
            }
            yield break;
        }

        foreach (var rawItem in items)
        {
            position++;
            var item = rawItem;
            if (binding.TypeDeclaration != null && IsAtomicForTarget(binding.TypeDeclaration.ItemType))
            {
                item = context.AtomizeWithNodes(item);
                item = TypeCastHelper.CastValue(item, binding.TypeDeclaration.ItemType);
            }
            else if (binding.TypeDeclaration != null)
            {
                // Node/element/attribute/etc. target — require the item to be a node
                var it = binding.TypeDeclaration.ItemType;
                if (it is Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                    or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                    or Ast.ItemType.ProcessingInstruction)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004",
                            $"For binding requires {it}, got {item?.GetType().Name ?? "empty"}");
                }
            }
            var tuple = new Dictionary<QName, object?> { [binding.Variable] = item };
            if (binding.PositionalVariable.HasValue)
            {
                tuple[binding.PositionalVariable.Value] = position;
            }

            context.PushScope();
            context.BindVariable(binding.Variable, item);
            if (binding.PositionalVariable.HasValue)
            {
                context.BindVariable(binding.PositionalVariable.Value, position);
            }

            try
            {
                await foreach (var rest in ExecuteBindingsAsync(context, index + 1))
                {
                    var merged = new Dictionary<QName, object?>(tuple);
                    foreach (var (name, value) in rest)
                        merged[name] = value;
                    yield return merged;
                }
            }
            finally
            {
                context.PopScope();
            }
        }
    }
}

/// <summary>
/// For binding operator.
/// </summary>
public sealed class ForBindingOperator
{
    public required QName Variable { get; init; }
    public QName? PositionalVariable { get; init; }
    public bool AllowingEmpty { get; init; }
    public required PhysicalOperator InputOperator { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
}

/// <summary>
/// Let clause operator.
/// </summary>
public sealed class LetClauseOperator : FlworClauseOperator
{
    public required IReadOnlyList<LetBindingOperator> Bindings { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        var tuple = new Dictionary<QName, object?>();

        foreach (var binding in Bindings)
        {
            var values = new List<object?>();
            await foreach (var item in binding.InputOperator.ExecuteAsync(context))
            {
                values.Add(item);
            }

            // Let binds the entire sequence
            object? value = values.Count switch
            {
                0 => null,
                1 => values[0],
                _ => values.ToArray()
            };

            if (binding.TypeDeclaration != null)
            {
                var td = binding.TypeDeclaration;
                // XQuery §3.8.1: let clause type declaration uses SequenceType matching
                // (no promotion, no untypedAtomic casting — stricter than function coercion)
                TypeCastHelper.RequireSequenceTypeMatch(value, td, $"let ${binding.Variable.LocalName}");
            }

            tuple[binding.Variable] = value;
            context.BindVariable(binding.Variable, value);
        }

        yield return tuple;
    }
}

/// <summary>
/// Let binding operator.
/// </summary>
public sealed class LetBindingOperator
{
    public required QName Variable { get; init; }
    public required PhysicalOperator InputOperator { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
}

/// <summary>
/// Where clause operator.
/// </summary>
public sealed class WhereClauseOperator : FlworClauseOperator
{
    public required PhysicalOperator ConditionOperator { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        // EBV rules: if 2+ items and the first is not a node → FORG0006.
        // If first is a node → EBV is true (shortcut).
        object? first = null;
        bool hasFirst = false;
        await foreach (var item in ConditionOperator.ExecuteAsync(context))
        {
            if (!hasFirst)
            {
                first = item;
                hasFirst = true;
                // If first item is a node, EBV is true regardless of remaining items
                if (first is Xdm.Nodes.XdmNode or Xdm.TextNodeItem
                    or System.Xml.XmlNode or System.Xml.Linq.XNode)
                    break;
            }
            else
            {
                // 2+ items, first is not a node → FORG0006
                throw new XQueryRuntimeException("FORG0006",
                    "Effective boolean value not defined for a sequence of two or more items starting with a non-node value");
            }
        }

        if (QueryExecutionContext.EffectiveBooleanValue(first))
        {
            yield return new Dictionary<QName, object?>();
        }
    }
}

/// <summary>
/// Order by clause operator.
/// </summary>
public sealed class OrderByClauseOperator : FlworClauseOperator
{
    public bool Stable { get; init; }
    public required IReadOnlyList<OrderSpecOperator> OrderSpecs { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        // Order by is handled at the FLWOR level, not here
        // This clause just passes through
        await Task.CompletedTask;
        yield return new Dictionary<QName, object?>();
    }
}

/// <summary>
/// Order spec operator.
/// </summary>
public sealed class OrderSpecOperator
{
    public required PhysicalOperator KeyOperator { get; init; }
    public OrderDirection Direction { get; init; }
    public EmptyOrder EmptyOrder { get; init; }
    public string? Collation { get; init; }
}

/// <summary>
/// Group by clause operator.
/// </summary>
public sealed class GroupByClauseOperator : FlworClauseOperator
{
    public required IReadOnlyList<GroupingSpecOperator> GroupingSpecs { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        // Group by is complex - simplified pass-through
        await Task.CompletedTask;
        yield return new Dictionary<QName, object?>();
    }
}

/// <summary>
/// Grouping spec operator.
/// </summary>
public sealed class GroupingSpecOperator
{
    public required QName Variable { get; init; }
    public PhysicalOperator? KeyOperator { get; init; }
    public Ast.XdmSequenceType? TypeDeclaration { get; init; }
    public string? Collation { get; init; }
}

/// <summary>
/// Count clause operator.
/// </summary>
public sealed class CountClauseOperator : FlworClauseOperator
{
    public required QName Variable { get; init; }
    private long _counter;

    public long CurrentCount => _counter;
    public void IncrementCounter() => _counter++;
    public void ResetCounter() => _counter = 0;

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        IncrementCounter();
        yield return new Dictionary<QName, object?> { [Variable] = CurrentCount };
    }
}

/// <summary>
/// Window clause operator (tumbling or sliding window).
/// </summary>
public sealed class WindowClauseOperator : FlworClauseOperator
{
    public required Ast.WindowKind Kind { get; init; }
    public required QName Variable { get; init; }
    public Ast.XdmSequenceType? TypeDeclaration { get; init; }
    public bool OnlyEnd { get; init; }
    public required PhysicalOperator InputOperator { get; init; }
    public required WindowConditionOperator StartCondition { get; init; }
    public WindowConditionOperator? EndCondition { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        // Materialize the input sequence
        var items = new List<object?>();
        await foreach (var item in InputOperator.ExecuteAsync(context))
            items.Add(item);

        if (Kind == Ast.WindowKind.Tumbling)
        {
            await foreach (var window in ExecuteTumblingAsync(items, context))
                yield return window;
        }
        else
        {
            await foreach (var window in ExecuteSlidingAsync(items, context))
                yield return window;
        }
    }

    private async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteTumblingAsync(
        List<object?> items, QueryExecutionContext context)
    {
        bool inWindow = false;
        int windowStartPos = 0;
        object? windowStartItem = null;
        object? windowStartPrev = null;
        object? windowStartNext = null;
        var windowItems = new List<object?>();

        for (int i = 0; i < items.Count; i++)
        {
            var cur = items[i];
            var prev = i > 0 ? items[i - 1] : null;
            var next = i + 1 < items.Count ? items[i + 1] : null;
            var pos = i + 1; // 1-based

            if (!inWindow)
            {
                // No window open — check start condition
                if (await EvaluateConditionAsync(StartCondition, cur, prev, next, pos, context))
                {
                    inWindow = true;
                    windowStartPos = pos;
                    windowStartItem = cur;
                    windowStartPrev = prev;
                    windowStartNext = next;
                    windowItems.Clear();
                    windowItems.Add(cur);

                    // For windows with an end condition, check if end fires on the start item too
                    if (EndCondition != null)
                    {
                        bool shouldEnd = await EvaluateConditionWithStartVarsAsync(
                            EndCondition, cur, prev, next, pos,
                            StartCondition, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, context);
                        if (shouldEnd)
                        {
                            var t = MakeWindowTuple(windowItems, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, cur, prev, next, pos);
                            if (t != null) yield return t;
                            inWindow = false;
                            windowItems.Clear();
                        }
                    }
                }
            }
            else
            {
                // Window is open — add item and check end condition
                windowItems.Add(cur);

                if (EndCondition != null)
                {
                    bool shouldEnd = await EvaluateConditionWithStartVarsAsync(
                        EndCondition, cur, prev, next, pos,
                        StartCondition, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, context);
                    if (shouldEnd)
                    {
                        var t = MakeWindowTuple(windowItems, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, cur, prev, next, pos);
                        if (t != null) yield return t;
                        inWindow = false;
                        windowItems.Clear();
                    }
                }
                else
                {
                    // No end condition — check if a new start condition fires (closes current window)
                    bool startNew = await EvaluateConditionAsync(StartCondition, cur, prev, next, pos, context);
                    if (startNew)
                    {
                        // Remove the current item from old window — it starts the new one
                        windowItems.RemoveAt(windowItems.Count - 1);
                        if (windowItems.Count > 0)
                        {
                            // Last closed item is the one before the new start (at index i-1).
                            var lastIdx = i - 1; // 0-based
                            var lastItem = items[lastIdx];
                            var lastPrev = lastIdx > 0 ? items[lastIdx - 1] : null;
                            var lastNext = cur;
                            var t = MakeWindowTuple(windowItems, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, lastItem, lastPrev, lastNext, lastIdx + 1);
                            if (t != null) yield return t;
                        }

                        windowStartPos = pos;
                        windowStartItem = cur;
                        windowStartPrev = prev;
                        windowStartNext = next;
                        windowItems.Clear();
                        windowItems.Add(cur);
                    }
                }
            }
        }

        // Flush remaining window (only if not "only end" — an unclosed window is dropped under "only end")
        if (inWindow && windowItems.Count > 0 && !OnlyEnd)
        {
            // Per spec, end variables bind to the last item when the sequence is exhausted.
            var lastIdx = items.Count - 1;
            var lastItem = items[lastIdx];
            var lastPrev = lastIdx > 0 ? items[lastIdx - 1] : null;
            object? lastNext = null;
            var t = MakeWindowTuple(windowItems, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, lastItem, lastPrev, lastNext, lastIdx + 1);
            if (t != null) yield return t;
        }
    }

    private async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteSlidingAsync(
        List<object?> items, QueryExecutionContext context)
    {
        // Sliding windows: for each position where start condition is true,
        // open a new window. Each window independently ends where end condition is true.
        for (int i = 0; i < items.Count; i++)
        {
            var sCur = items[i];
            var sPrev = i > 0 ? items[i - 1] : null;
            var sNext = i + 1 < items.Count ? items[i + 1] : null;
            var sPos = i + 1;

            if (!await EvaluateConditionAsync(StartCondition, sCur, sPrev, sNext, sPos, context))
                continue;

            // Start a window here
            var windowItems = new List<object?> { sCur };
            bool ended = false;
            object? endCur = null, endPrev = null, endNext = null;
            int endPos = 0;

            // Check end condition on the start item itself (zero-length windows)
            if (EndCondition != null &&
                await EvaluateConditionWithStartVarsAsync(
                    EndCondition, sCur, sPrev, sNext, sPos,
                    StartCondition, sCur, sPrev, sNext, sPos, context))
            {
                ended = true;
                endCur = sCur; endPrev = sPrev; endNext = sNext; endPos = sPos;
            }
            else
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    var eCur = items[j];
                    var ePrev = items[j - 1];
                    var eNext = j + 1 < items.Count ? items[j + 1] : null;
                    var ePos = j + 1;

                    windowItems.Add(eCur);

                    if (EndCondition != null &&
                        await EvaluateConditionWithStartVarsAsync(
                            EndCondition, eCur, ePrev, eNext, ePos,
                            StartCondition, sCur, sPrev, sNext, sPos, context))
                    {
                        ended = true;
                        endCur = eCur; endPrev = ePrev; endNext = eNext; endPos = ePos;
                        break;
                    }
                }
            }

            if (ended)
            {
                var t = MakeWindowTuple(windowItems, sCur, sPrev, sNext, sPos, endCur, endPrev, endNext, endPos);
                if (t != null) yield return t;
            }
            else if (!OnlyEnd)
            {
                // No end condition fired. Emit the window (unclosed) unless OnlyEnd is set.
                // Per spec, end variables bind to the last item when sequence is exhausted.
                var lastIdx = items.Count - 1;
                var lastItem = items[lastIdx];
                var lastPrev = lastIdx > 0 ? items[lastIdx - 1] : null;
                object? lastNext = null;
                var t = MakeWindowTuple(windowItems, sCur, sPrev, sNext, sPos, lastItem, lastPrev, lastNext, lastIdx + 1);
                if (t != null) yield return t;
            }
        }
    }

    private Dictionary<QName, object?>? MakeWindowTuple(
        List<object?> windowItems,
        object? startItem, object? startPrev, object? startNext, int startPos,
        object? endItem, object? endPrev, object? endNext, int endPos)
    {
        // Build window value. Always expose as a sequence (list).
        // If the window is a single item, still pass it as-is for scalar access but wrap list for count().
        object? value;
        if (windowItems.Count == 0) value = null;
        else if (windowItems.Count == 1) value = windowItems[0];
        else value = windowItems.ToArray();

        // Enforce type declaration on the window variable.
        if (TypeDeclaration != null)
        {
            if (!CheckWindowType(value, windowItems.Count, TypeDeclaration))
                throw new XQueryRuntimeException("XPTY0004", "Window value does not match declared type");
        }

        var tuple = new Dictionary<QName, object?> { [Variable] = value };

        if (StartCondition.CurrentItem is { } sCurVar) tuple[sCurVar] = startItem;
        if (StartCondition.Position is { } sPosVar) tuple[sPosVar] = (long)startPos;
        if (StartCondition.PreviousItem is { } sPrevVar) tuple[sPrevVar] = startPrev;
        if (StartCondition.NextItem is { } sNextVar) tuple[sNextVar] = startNext;

        if (EndCondition != null)
        {
            if (EndCondition.CurrentItem is { } eCurVar) tuple[eCurVar] = endItem;
            if (EndCondition.Position is { } ePosVar) tuple[ePosVar] = (long)endPos;
            if (EndCondition.PreviousItem is { } ePrevVar) tuple[ePrevVar] = endPrev;
            if (EndCondition.NextItem is { } eNextVar) tuple[eNextVar] = endNext;
        }

        return tuple;
    }

    private static bool CheckWindowType(object? value, int count, Ast.XdmSequenceType type)
    {
        var items = new List<object?>();
        if (value != null)
        {
            if (value is string) items.Add(value);
            else if (value is System.Collections.IEnumerable e)
            {
                foreach (var x in e) items.Add(x);
            }
            else items.Add(value);
        }
        return TypeCastHelper.MatchesType(items, type);
    }

    /// <summary>
    /// Evaluates the end condition with start condition variables also bound,
    /// so end conditions can reference $spos, $s, etc. from the start clause.
    /// </summary>
    private static async ValueTask<bool> EvaluateConditionWithStartVarsAsync(
        WindowConditionOperator endCondition,
        object? cur, object? prev, object? next, int pos,
        WindowConditionOperator startCondition,
        object? startItem, object? startPrev, object? startNext, int startPos,
        QueryExecutionContext context)
    {
        context.PushScope();
        try
        {
            // Bind start condition variables so end condition can reference them
            if (startCondition.CurrentItem.HasValue)
                context.BindVariable(startCondition.CurrentItem.Value, startItem);
            if (startCondition.Position.HasValue)
                context.BindVariable(startCondition.Position.Value, (long)startPos);
            if (startCondition.PreviousItem.HasValue)
                context.BindVariable(startCondition.PreviousItem.Value, startPrev);
            if (startCondition.NextItem.HasValue)
                context.BindVariable(startCondition.NextItem.Value, startNext);

            // Bind end condition variables
            if (endCondition.CurrentItem.HasValue)
                context.BindVariable(endCondition.CurrentItem.Value, cur);
            if (endCondition.PreviousItem.HasValue)
                context.BindVariable(endCondition.PreviousItem.Value, prev);
            if (endCondition.NextItem.HasValue)
                context.BindVariable(endCondition.NextItem.Value, next);
            if (endCondition.Position.HasValue)
                context.BindVariable(endCondition.Position.Value, (long)pos);

            object? result = null;
            await foreach (var item in endCondition.WhenOperator.ExecuteAsync(context))
            {
                result = item;
                break;
            }
            return QueryExecutionContext.EffectiveBooleanValue(result);
        }
        finally
        {
            context.PopScope();
        }
    }

    private static async ValueTask<bool> EvaluateConditionAsync(
        WindowConditionOperator condition,
        object? cur, object? prev, object? next, int pos,
        QueryExecutionContext context)
    {
        context.PushScope();
        try
        {
            if (condition.CurrentItem.HasValue)
                context.BindVariable(condition.CurrentItem.Value, cur);
            if (condition.PreviousItem.HasValue)
                context.BindVariable(condition.PreviousItem.Value, prev);
            if (condition.NextItem.HasValue)
                context.BindVariable(condition.NextItem.Value, next);
            if (condition.Position.HasValue)
                context.BindVariable(condition.Position.Value, (long)pos);

            object? result = null;
            await foreach (var item in condition.WhenOperator.ExecuteAsync(context))
            {
                result = item;
                break;
            }
            return QueryExecutionContext.EffectiveBooleanValue(result);
        }
        finally
        {
            context.PopScope();
        }
    }
}

/// <summary>
/// Window condition operator (start or end condition).
/// </summary>
public sealed class WindowConditionOperator
{
    public QName? CurrentItem { get; init; }
    public QName? PreviousItem { get; init; }
    public QName? NextItem { get; init; }
    public QName? Position { get; init; }
    public required PhysicalOperator WhenOperator { get; init; }
}

/// <summary>
/// Function call operator.
/// </summary>
public sealed class FunctionCallOperator : PhysicalOperator
{
    public XQueryFunction? Function { get; init; }
    public required QName FunctionName { get; init; }
    public required IReadOnlyList<PhysicalOperator> ArgumentOperators { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Resolve function: prefer runtime-registered functions (e.g. user-declared)
        // over the statically-resolved reference (which may be a placeholder).
        var function = context.Functions.Resolve(FunctionName, ArgumentOperators.Count)
            ?? Function;

        if (function == null)
        {
            throw new XQueryRuntimeException("XPST0017",
                $"Function {FunctionName.LocalName} not found");
        }

        // Streaming optimization for aggregate functions that don't need full materialization.
        // fn:count streams through the argument counting items without storing them.
        // fn:exists/fn:empty only need to check if the first item exists.
        if (function is CountFunction && ArgumentOperators.Count == 1)
        {
            long itemCount = 0;
            await foreach (var item in ArgumentOperators[0].ExecuteAsync(context))
            {
                itemCount++;
                if (itemCount % 65536 == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
            }
            yield return itemCount;
            yield break;
        }
        if (function is ExistsFunction && ArgumentOperators.Count == 1)
        {
            var hasAny = false;
            await foreach (var item in ArgumentOperators[0].ExecuteAsync(context))
            {
                hasAny = true;
                break;
            }
            yield return hasAny;
            yield break;
        }
        if (function is EmptyFunction && ArgumentOperators.Count == 1)
        {
            var hasAny = false;
            await foreach (var item in ArgumentOperators[0].ExecuteAsync(context))
            {
                hasAny = true;
                break;
            }
            yield return !hasAny;
            yield break;
        }

        // Evaluate arguments
        var args = new object?[ArgumentOperators.Count];
        for (var ai = 0; ai < ArgumentOperators.Count; ai++)
        {
            object? singleItem = null;
            List<object?>? multiItems = null;
            var count = 0;
            await foreach (var item in ArgumentOperators[ai].ExecuteAsync(context))
            {
                count++;
                if (count == 1)
                {
                    singleItem = item;
                }
                else
                {
                    if (count == 2)
                    {
                        multiItems = [singleItem, item];
                    }
                    else
                    {
                        multiItems!.Add(item);
                    }
                }
                // Guard against unbounded materialization
                if (count % 65536 == 0)
                    context.CheckMaterializationLimit(count);
            }
            args[ai] = count switch
            {
                0 => null,
                1 => singleItem,
                _ => multiItems!.ToArray()
            };
        }

        // XPath 1.0 backwards-compat: coerce arguments to expected types.
        // Multi-item sequence → first item (for single-item parameters).
        // Empty nodeset → NaN for xs:double, empty string for xs:string.
        if (context.BackwardsCompatible && function.Parameters is { Count: > 0 })
        {
            for (var bi = 0; bi < args.Length && bi < function.Parameters.Count; bi++)
            {
                var paramType = function.Parameters[bi].Type;
                if (args[bi] is null)
                {
                    var itemType = paramType?.ItemType;
                    if (itemType is Ast.ItemType.Double or Ast.ItemType.Float or Ast.ItemType.Decimal or Ast.ItemType.Integer)
                        args[bi] = double.NaN;
                    else if (itemType is Ast.ItemType.String)
                        args[bi] = "";
                }
                else if (args[bi] is object[] arr && arr.Length > 0
                    && paramType?.Occurrence is Ast.Occurrence.ExactlyOne or Ast.Occurrence.ZeroOrOne)
                {
                    // BC mode: multi-item sequence passed to single-item parameter → take first item
                    args[bi] = arr[0];
                }
            }
        }

        // Invoke function
        var result = await function.InvokeAsync(args, context);

        // XDM arrays (List<object?>) and maps (Dictionary) are items, not sequences — yield as-is
        if (result is IDictionary<object, object?> || result is List<object?>)
        {
            yield return result;
        }
        else if (result is IEnumerable<object?> seq)
        {
            foreach (var item in seq)
                yield return item;
        }
        else if (result != null)
        {
            yield return result;
        }
    }
}

/// <summary>
/// Binary operator node.
/// </summary>
public sealed class BinaryOperatorNode : PhysicalOperator
{
    public required PhysicalOperator Left { get; init; }
    public required PhysicalOperator Right { get; init; }
    public required BinaryOperator Operator { get; init; }

    // Thread-local default string comparison from execution context (set at start of ExecuteAsync)
    private StringComparison _stringComparison;
    private bool _backwardsCompatible;

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Resolve default collation for string comparisons
        _stringComparison = context.DefaultCollation != null
            ? Functions.CollationHelper.GetStringComparison(context.DefaultCollation)
            : StringComparison.Ordinal;
        _backwardsCompatible = context.BackwardsCompatible;
        // Union/Intersect/Except are sequence operations, not scalar operations
        if (Operator is BinaryOperator.Union)
        {
            // Union must return nodes in document order with duplicates eliminated
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var items = new List<object>();
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", $"An operand of the union operator is not a node");
                    if (seen.Add(item))
                        items.Add(item);
                }
            }
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", $"An operand of the union operator is not a node");
                    if (seen.Add(item))
                        items.Add(item);
                }
            }
            // Sort by document order (NodeId) if all items are XdmNodes
            if (items.Count > 1 && items[0] is Xdm.Nodes.XdmNode)
            {
                items.Sort((a, b) =>
                {
                    if (a is Xdm.Nodes.XdmNode na && b is Xdm.Nodes.XdmNode nb)
                        return na.Id.CompareTo(nb.Id);
                    return 0;
                });
            }
            foreach (var item in items)
                yield return item;
            yield break;
        }

        if (Operator is BinaryOperator.Intersect)
        {
            var rightItems = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", "An operand of the intersect operator is not a node");
                    rightItems.Add(item);
                }
            }
            var resultItems = new List<object>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", "An operand of the intersect operator is not a node");
                    if (rightItems.Contains(item) && seen.Add(item))
                        resultItems.Add(item);
                }
            }
            // Return in document order
            if (resultItems.Count > 1)
            {
                resultItems.Sort((a, b) =>
                {
                    if (a is Xdm.Nodes.XdmNode na && b is Xdm.Nodes.XdmNode nb)
                        return na.Id.CompareTo(nb.Id);
                    return 0;
                });
            }
            foreach (var item in resultItems)
                yield return item;
            yield break;
        }

        if (Operator is BinaryOperator.Except)
        {
            var rightItems = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", "An operand of the except operator is not a node");
                    rightItems.Add(item);
                }
            }
            var resultItems = new List<object>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", "An operand of the except operator is not a node");
                    if (!rightItems.Contains(item) && seen.Add(item))
                        resultItems.Add(item);
                }
            }
            // Return in document order
            if (resultItems.Count > 1)
            {
                resultItems.Sort((a, b) =>
                {
                    if (a is Xdm.Nodes.XdmNode na && b is Xdm.Nodes.XdmNode nb)
                        return na.Id.CompareTo(nb.Id);
                    return 0;
                });
            }
            foreach (var item in resultItems)
                yield return item;
            yield break;
        }

        // General comparisons use existential semantics: iterate all items in
        // both operands and return true if ANY pair satisfies the comparison.
        if (Operator is BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual or
            BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
            BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual)
        {
            var leftItems = new List<object?>();
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                // XQuery 3.1: arrays in general comparison operands are atomized and expanded
                if (item is List<object?> arr)
                {
                    var atomized = QueryExecutionContext.AtomizeTyped(item);
                    if (atomized is object?[] seq) leftItems.AddRange(seq);
                    else if (atomized != null) leftItems.Add(atomized);
                }
                else
                    leftItems.Add(item);
            }

            var rightItemsList = new List<object?>();
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item is List<object?> arr)
                {
                    var atomized = QueryExecutionContext.AtomizeTyped(item);
                    if (atomized is object?[] seq) rightItemsList.AddRange(seq);
                    else if (atomized != null) rightItemsList.Add(atomized);
                }
                else
                    rightItemsList.Add(item);
            }

            // If either operand is empty, general comparison is false
            if (leftItems.Count == 0 || rightItemsList.Count == 0)
            {
                yield return false;
                yield break;
            }

            // XPath 1.0 backwards-compat: if either operand contains a boolean
            // and the operator is = or !=, convert both to boolean before comparison.
            // For relational operators (<, >, <=, >=), XPath 1.0 always converts to numbers.
            if (context.BackwardsCompatible && Operator is BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual)
            {
                bool anyBoolLeft = leftItems.Any(i => context.AtomizeWithNodes(i) is bool);
                bool anyBoolRight = rightItemsList.Any(i => context.AtomizeWithNodes(i) is bool);
                if (anyBoolLeft || anyBoolRight)
                {
                    // Compare as booleans using effective boolean values
                    var leftEbv = QueryExecutionContext.EffectiveBooleanValue(
                        leftItems.Count == 1 ? leftItems[0] : leftItems.ToArray());
                    var rightEbv = QueryExecutionContext.EffectiveBooleanValue(
                        rightItemsList.Count == 1 ? rightItemsList[0] : rightItemsList.ToArray());
                    var boolResult = Operator is BinaryOperator.GeneralEqual
                        ? leftEbv == rightEbv
                        : leftEbv != rightEbv;
                    yield return boolResult;
                    yield break;
                }
            }

            // XPath 1.0 backwards-compat: for <, >, <=, >= when neither operand
            // is a node-set, convert both to numbers before comparison.
            // For = and !=, if at least one operand is a number, convert both to numbers.
            bool bc1NumericConvert = context.BackwardsCompatible && Operator is
                BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
                BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual;
            bool bc1EqNumericConvert = context.BackwardsCompatible && Operator is
                BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual;

            // Check all pairs for existential match
            foreach (var l in leftItems)
            {
                foreach (var r in rightItemsList)
                {
                    var lv = QueryExecutionContext.AtomizeTyped(l);
                    var rv = QueryExecutionContext.AtomizeTyped(r);
                    // XPath 1.0: ordering operators always convert to number;
                    // equality operators convert to number if either operand is numeric
                    if (bc1NumericConvert ||
                        (bc1EqNumericConvert && (IsNumericOrUntyped(lv) || IsNumericOrUntyped(rv))))
                    {
                        lv = CoerceToDouble(lv);
                        rv = CoerceToDouble(rv);
                    }
                    else if (!context.BackwardsCompatible)
                    {
                        // XPath 2.0+ general comparison: handle xs:untypedAtomic casting
                        (lv, rv) = CastUntypedForGeneralComparison(lv, rv);
                    }
                    var pairResult = EvaluateBinary(lv, rv);
                    if (pairResult is true)
                    {
                        yield return true;
                        yield break;
                    }
                }
            }
            yield return false;
            yield break;
        }

        // Short-circuit logical operators: do not evaluate right operand
        // when left operand already determines the result.
        if (Operator is BinaryOperator.Or)
        {
            object? leftVal = null;
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            { leftVal = item; break; }
            if (QueryExecutionContext.EffectiveBooleanValue(leftVal))
            {
                yield return true;
                yield break;
            }
            object? rightVal = null;
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            { rightVal = item; break; }
            yield return QueryExecutionContext.EffectiveBooleanValue(rightVal);
            yield break;
        }

        // XPath 4.0 otherwise: return left sequence if non-empty, else right sequence
        if (Operator is BinaryOperator.Otherwise)
        {
            var hasLeft = false;
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                hasLeft = true;
                yield return item;
            }
            if (!hasLeft)
            {
                await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
                    yield return item;
            }
            yield break;
        }

        if (Operator is BinaryOperator.And)
        {
            object? leftVal = null;
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            { leftVal = item; break; }
            if (!QueryExecutionContext.EffectiveBooleanValue(leftVal))
            {
                yield return false;
                yield break;
            }
            object? rightVal = null;
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            { rightVal = item; break; }
            yield return QueryExecutionContext.EffectiveBooleanValue(rightVal);
            yield break;
        }

        // Value comparisons (eq, ne, lt, etc.) and other operators: single item
        object? leftValue = null;
        int leftCount = 0;
        await foreach (var item in Left.ExecuteAsync(context))
        {
            // XQuery 3.1: arrays are atomized and expanded for value comparisons
            if (item is List<object?> leftArr)
            {
                var atomized = QueryExecutionContext.AtomizeTyped(item);
                if (atomized is object?[] leftSeq)
                {
                    foreach (var ai in leftSeq)
                    {
                        if (leftCount == 0) leftValue = ai;
                        leftCount++;
                        if (leftCount > 1) break;
                    }
                }
                else if (atomized != null)
                {
                    if (leftCount == 0) leftValue = atomized;
                    leftCount++;
                }
                // else: empty array → contributes nothing (leftCount stays 0)
            }
            else
            {
                if (leftCount == 0)
                    leftValue = item;
                leftCount++;
            }
            if (leftCount > 1)
                break;
        }

        object? rightValue = null;
        int rightCount = 0;
        await foreach (var item in Right.ExecuteAsync(context))
        {
            // XQuery 3.1: arrays are atomized and expanded for value comparisons
            if (item is List<object?> rightArr)
            {
                var atomized = QueryExecutionContext.AtomizeTyped(item);
                if (atomized is object?[] rightSeq)
                {
                    foreach (var ai in rightSeq)
                    {
                        if (rightCount == 0) rightValue = ai;
                        rightCount++;
                        if (rightCount > 1) break;
                    }
                }
                else if (atomized != null)
                {
                    if (rightCount == 0) rightValue = atomized;
                    rightCount++;
                }
            }
            else
            {
                if (rightCount == 0)
                    rightValue = item;
                rightCount++;
            }
            if (rightCount > 1)
                break;
        }

        // XPTY0004: Value comparisons require single item operands
        if ((leftCount > 1 || rightCount > 1) && Operator is
            BinaryOperator.Equal or BinaryOperator.NotEqual or
            BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
            BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual)
        {
            throw new XQueryRuntimeException("XPTY0004",
                "A value comparison operand is a sequence of more than one item");
        }

        // XPTY0004: arithmetic operators require singleton atomic operands
        if ((leftCount > 1 || rightCount > 1) && Operator is
            BinaryOperator.Add or BinaryOperator.Subtract or
            BinaryOperator.Multiply or BinaryOperator.Divide or
            BinaryOperator.IntegerDivide or BinaryOperator.Modulo)
        {
            throw new XQueryRuntimeException("XPTY0004",
                "An arithmetic operand is a sequence of more than one item");
        }

        // Value comparisons return empty sequence when either operand is empty
        if ((leftValue is null || rightValue is null) && Operator is
            BinaryOperator.Equal or BinaryOperator.NotEqual or
            BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
            BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual or
            BinaryOperator.Is or BinaryOperator.Precedes or BinaryOperator.Follows)
        {
            // Empty sequence — yield nothing
            yield break;
        }

        // XPath 3.1 §4.2: arithmetic operators return the empty sequence when either
        // operand is the empty sequence (leftCount==0 or rightCount==0). Skip this in
        // XPath 1.0 backwards-compatible mode, which coerces empty → NaN.
        if ((leftCount == 0 || rightCount == 0) && !context.BackwardsCompatible && Operator is
            BinaryOperator.Add or BinaryOperator.Subtract or
            BinaryOperator.Multiply or BinaryOperator.Divide or
            BinaryOperator.IntegerDivide or BinaryOperator.Modulo)
        {
            yield break;
        }

        // XPath 1.0 backwards-compatible: arithmetic always uses doubles,
        // and empty sequences become NaN.
        if (context.BackwardsCompatible && Operator is
            BinaryOperator.Add or BinaryOperator.Subtract or
            BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo)
        {
            var ld = CoerceToDouble(context.AtomizeWithNodes(leftValue));
            var rd = CoerceToDouble(context.AtomizeWithNodes(rightValue));
            yield return EvaluateBinary(ld, rd);
            yield break;
        }

        var result = EvaluateBinary(leftValue, rightValue);
        yield return result;
    }

    private object? EvaluateBinary(object? left, object? right)
    {
        // General comparisons use existential semantics: if either operand is an
        // empty sequence (null), the result is always false (no pairs to compare).
        if ((left is null || right is null) && Operator is
            BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual or
            BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
            BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual)
        {
            return false;
        }

        // Node comparison operators must use un-atomized node values
        if (Operator is BinaryOperator.Is or BinaryOperator.Precedes or BinaryOperator.Follows)
        {
            // Empty sequence operand → empty sequence result
            if (left is null || right is null)
                return null;
            var leftNode = left as Xdm.Nodes.XdmNode;
            var rightNode = right as Xdm.Nodes.XdmNode;
            // Non-empty, non-node operand → XPTY0004
            if (leftNode is null || rightNode is null)
                throw new XQueryRuntimeException("XPTY0004",
                    "Operands of node comparison operator must be nodes");
            return Operator switch
            {
                BinaryOperator.Is => ReferenceEquals(leftNode, rightNode) || leftNode.Id == rightNode.Id,
                BinaryOperator.Precedes => leftNode.Id < rightNode.Id,
                BinaryOperator.Follows => leftNode.Id > rightNode.Id,
                _ => null
            };
        }

        // Atomize XDM nodes for all binary operations, preserving xs:untypedAtomic type
        left = QueryExecutionContext.AtomizeTyped(left);
        right = QueryExecutionContext.AtomizeTyped(right);

        // XPath 2.0+ type handling for xs:untypedAtomic (when not in backwards-compatible mode)
        if (!_backwardsCompatible)
        {
            bool isArithmetic = Operator is BinaryOperator.Add or BinaryOperator.Subtract or
                BinaryOperator.Multiply or BinaryOperator.Divide or
                BinaryOperator.IntegerDivide or BinaryOperator.Modulo;
            bool isValueComparison = Operator is BinaryOperator.Equal or BinaryOperator.NotEqual or
                BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
                BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual;
            bool isGeneralComparison = Operator is BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual or
                BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
                BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual;

            if (isArithmetic)
            {
                // XPath 3.1 Section 4.2: xs:untypedAtomic → cast to xs:double for arithmetic
                if (left is Xdm.XsUntypedAtomic lua)
                    left = ToDoubleOrThrow(lua.Value);
                if (right is Xdm.XsUntypedAtomic rua)
                    right = ToDoubleOrThrow(rua.Value);
                // XPTY0004: xs:string is not a valid operand for arithmetic (XPath 2.0+)
                if (left is string || right is string)
                    throw new XQueryRuntimeException("XPTY0004",
                        "Arithmetic operators are not defined for xs:string");
            }
            else if (isValueComparison)
            {
                // XPath 3.1 Section 3.7.2: xs:untypedAtomic → always cast to xs:string
                // (Unlike general comparisons which cast to the other operand's type)
                if (left is Xdm.XsUntypedAtomic lua)
                    left = lua.Value;
                if (right is Xdm.XsUntypedAtomic rua)
                    right = rua.Value;
            }
            // For general comparisons, XsUntypedAtomic is already handled in the loop above
            // (CastUntypedForGeneralComparison). Any remaining XsUntypedAtomic → extract value.
            if (left is Xdm.XsUntypedAtomic lua3)
                left = lua3.Value;
            if (right is Xdm.XsUntypedAtomic rua3)
                right = rua3.Value;
            // Unwrap XsTypedString to plain string for comparison/arithmetic
            if (left is Xdm.XsTypedString tsL)
                left = tsL.Value;
            if (right is Xdm.XsTypedString tsR)
                right = tsR.Value;

            // XPTY0004: incompatible types for comparison/arithmetic (XPath 2.0+)
            if (isValueComparison || isGeneralComparison)
            {
                bool leftIsBool = left is bool;
                bool rightIsBool = right is bool;
                bool leftIsStr = left is string;
                bool rightIsStr = right is string;
                bool leftIsNum = IsNumeric(left);
                bool rightIsNum = IsNumeric(right);

                if ((leftIsBool && !rightIsBool && right is not null) ||
                    (rightIsBool && !leftIsBool && left is not null))
                    throw new XQueryRuntimeException("XPTY0004",
                        "Cannot compare xs:boolean with non-boolean type");
                // xs:string vs numeric → XPTY0004
                if ((leftIsStr && rightIsNum) || (rightIsStr && leftIsNum))
                    throw new XQueryRuntimeException("XPTY0004",
                        "Cannot compare xs:string with numeric type");
                // xs:anyURI vs numeric → XPTY0004
                bool leftIsUri = left is Xdm.XsAnyUri;
                bool rightIsUri = right is Xdm.XsAnyUri;
                if ((leftIsUri && rightIsNum) || (rightIsUri && leftIsNum))
                    throw new XQueryRuntimeException("XPTY0004",
                        "Cannot compare xs:anyURI with numeric type");
                // Cross-type date/time comparison → XPTY0004
                // (dateTime eq date, time eq date, etc.)
                if (isValueComparison)
                {
                    bool leftIsDate = left is Xdm.XsDateTime or Xdm.XsDate or Xdm.XsTime;
                    bool rightIsDate = right is Xdm.XsDateTime or Xdm.XsDate or Xdm.XsTime;
                    if (leftIsDate && rightIsDate && left!.GetType() != right!.GetType())
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Cannot compare {left.GetType().Name} with {right.GetType().Name}");
                }
            }
        }
        else
        {
            // Backwards-compatible mode: XsUntypedAtomic is just a string
            if (left is Xdm.XsUntypedAtomic lua)
                left = lua.Value;
            if (right is Xdm.XsUntypedAtomic rua)
                right = rua.Value;
        }

        // IEEE 754 NaN handling: NaN is not less than, equal to, or greater than anything.
        // .NET's CompareTo incorrectly orders NaN < everything, so handle NaN explicitly.
        if (IsComparisonOperator(Operator))
        {
            var leftIsNaN = left is double dl && double.IsNaN(dl) || left is float fl && float.IsNaN(fl);
            var rightIsNaN = right is double dr && double.IsNaN(dr) || right is float fr && float.IsNaN(fr);
            if (leftIsNaN || rightIsNaN)
            {
                return Operator is BinaryOperator.NotEqual or BinaryOperator.GeneralNotEqual
                    ? (object)true : false;
            }
        }

        return Operator switch
        {
            // Arithmetic
            BinaryOperator.Add => Add(left, right),
            BinaryOperator.Subtract => Subtract(left, right),
            BinaryOperator.Multiply => Multiply(left, right),
            BinaryOperator.Divide => Divide(left, right),
            BinaryOperator.IntegerDivide => IntegerDivide(left, right),
            BinaryOperator.Modulo => Modulo(left, right),

            // Comparisons
            BinaryOperator.Equal or BinaryOperator.GeneralEqual => ValueEquals(left, right, _stringComparison),
            BinaryOperator.NotEqual or BinaryOperator.GeneralNotEqual => !ValueEquals(left, right, _stringComparison),
            BinaryOperator.LessThan or BinaryOperator.GeneralLessThan => ValueCompare(left, right, _stringComparison) < 0,
            BinaryOperator.LessOrEqual or BinaryOperator.GeneralLessOrEqual => ValueCompare(left, right, _stringComparison) <= 0,
            BinaryOperator.GreaterThan or BinaryOperator.GeneralGreaterThan => ValueCompare(left, right, _stringComparison) > 0,
            BinaryOperator.GreaterOrEqual or BinaryOperator.GeneralGreaterOrEqual => ValueCompare(left, right, _stringComparison) >= 0,

            // Logical (And/Or now handled via short-circuit in ExecuteAsync;
            // this path only reached from general comparison pairs)
            BinaryOperator.And => QueryExecutionContext.EffectiveBooleanValue(left) &&
                                  QueryExecutionContext.EffectiveBooleanValue(right),
            BinaryOperator.Or => QueryExecutionContext.EffectiveBooleanValue(left) ||
                                 QueryExecutionContext.EffectiveBooleanValue(right),

            // String
            BinaryOperator.Concat => $"{left}{right}",

            _ => throw new XQueryRuntimeException("XPTY0004", $"Unsupported operator {Operator}")
        };
    }

    /// <summary>
    /// Converts a value to double (boxed) per XPath 1.0 rules for backwards-compatible comparisons.
    /// </summary>
    private static object? CoerceToDouble(object? value) => value switch
    {
        null => double.NaN,
        double d => d,
        float f => (double)f,
        long l => (double)l,
        int i => (double)i,
        decimal m => (double)m,
        bool b => b ? 1.0 : 0.0,
        Xdm.XsUntypedAtomic ua => double.TryParse(ua.Value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var dua) ? dua : double.NaN,
        string s => double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN,
        _ => double.NaN
    };

    /// <summary>
    /// Returns true if the value is numeric or xs:untypedAtomic (which may be coerced to numeric).
    /// Used in backwards-compatible general comparisons.
    /// </summary>
    private static bool IsNumericOrUntyped(object? value) =>
        value is long or int or double or float or decimal or BigInteger;

    private static bool IsDuration(object? value) =>
        value is Xdm.XsDuration or Xdm.YearMonthDuration or TimeSpan;

    private static (int months, TimeSpan dayTime) NormalizeDuration(object? value)
    {
        return value switch
        {
            Xdm.XsDuration d => (d.TotalMonths, d.DayTime),
            Xdm.YearMonthDuration ym => (ym.TotalMonths, TimeSpan.Zero),
            TimeSpan ts => (0, ts),
            _ => (0, TimeSpan.Zero)
        };
    }

    /// <summary>
    /// XPath 2.0+ general comparison: cast xs:untypedAtomic to the type of the other operand.
    /// Per XPath 3.1 Section 3.7.1.
    /// </summary>
    private static (object? left, object? right) CastUntypedForGeneralComparison(object? left, object? right)
    {
        var leftIsUntyped = left is Xdm.XsUntypedAtomic;
        var rightIsUntyped = right is Xdm.XsUntypedAtomic;

        if (leftIsUntyped && rightIsUntyped)
        {
            // Both untyped → compare as strings
            return (((Xdm.XsUntypedAtomic)left!).Value, ((Xdm.XsUntypedAtomic)right!).Value);
        }
        if (leftIsUntyped)
        {
            var ua = (Xdm.XsUntypedAtomic)left!;
            if (IsNumeric(right))
                return (ToDoubleOrThrow(ua.Value), right);
            if (right is bool)
                return (CastUntypedToBoolean(ua.Value), right);
            // Cast untyped to match date/time/duration/QName types (FORG0001 on failure)
            var rightItemType = GetItemTypeForValue(right);
            if (rightItemType != null)
                return (CastUntypedToType(ua.Value, rightItemType.Value), right);
            return (ua.Value, right); // Cast to string for string/other comparisons
        }
        if (rightIsUntyped)
        {
            var ua = (Xdm.XsUntypedAtomic)right!;
            if (IsNumeric(left))
                return (left, ToDoubleOrThrow(ua.Value));
            if (left is bool)
                return (left, CastUntypedToBoolean(ua.Value));
            // Cast untyped to match date/time/duration/QName types (FORG0001 on failure)
            var leftItemType = GetItemTypeForValue(left);
            if (leftItemType != null)
                return (left, CastUntypedToType(ua.Value, leftItemType.Value));
            return (left, ua.Value); // Cast to string for string/other comparisons
        }
        return (left, right); // Neither is untyped — no conversion needed
    }

    /// <summary>
    /// Casts an xs:untypedAtomic string value to xs:boolean per XPath casting rules.
    /// "true"/"1" → true, "false"/"0" → false, anything else → FORG0001.
    /// </summary>
    private static bool CastUntypedToBoolean(string value) => value.Trim() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw new XQueryRuntimeException("FORG0001",
            $"Cannot cast '{value}' to xs:boolean")
    };

    /// <summary>
    /// Casts an xs:untypedAtomic string value to a target type, wrapping parse errors as FORG0001.
    /// </summary>
    private static object? CastUntypedToType(string value, ItemType targetType)
    {
        try
        {
            return TypeCastHelper.CastValue(value, targetType);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException
            && ex is not XQueryRuntimeException)
        {
            throw new XQueryRuntimeException("FORG0001",
                $"Cannot cast '{value}' to {targetType}: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the ItemType for date/time/duration/QName values, or null for string/numeric/bool.
    /// Used to determine how to cast xs:untypedAtomic in general comparisons.
    /// </summary>
    private static ItemType? GetItemTypeForValue(object? value) => value switch
    {
        Xdm.XsDate => ItemType.Date,
        DateOnly => ItemType.Date,
        Xdm.XsDateTime => ItemType.DateTime,
        DateTimeOffset => ItemType.DateTime,
        Xdm.XsTime => ItemType.Time,
        TimeOnly => ItemType.Time,
        TimeSpan => ItemType.DayTimeDuration,
        Xdm.DayTimeDuration => ItemType.DayTimeDuration,
        Xdm.YearMonthDuration => ItemType.YearMonthDuration,
        Xdm.XsDuration => ItemType.Duration,
        Xdm.XsGYearMonth => ItemType.GYearMonth,
        Xdm.XsGYear => ItemType.GYear,
        Xdm.XsGMonthDay => ItemType.GMonthDay,
        Xdm.XsGDay => ItemType.GDay,
        Xdm.XsGMonth => ItemType.GMonth,
        Xdm.XsAnyUri => ItemType.AnyUri,
        Core.QName => ItemType.QName,
        Xdm.XdmValue xv when xv.Type == Xdm.XdmType.Base64Binary => ItemType.Base64Binary,
        Xdm.XdmValue xv2 when xv2.Type == Xdm.XdmType.HexBinary => ItemType.HexBinary,
        _ => null
    };

    private static (object? left, object? right) PromoteNumeric(object? left, object? right)
    {
        // Atomize XDM nodes before numeric promotion
        left = QueryExecutionContext.Atomize(left);
        right = QueryExecutionContext.Atomize(right);

        // Convert string values to doubles for numeric operations
        if (left is string ls)
            left = double.TryParse(ls, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ld) ? ld : double.NaN;
        if (right is string rs)
            right = double.TryParse(rs, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rd) ? rd : double.NaN;

        // If both are float (no double), stay in float to preserve xs:float type
        if (left is float lf && right is float rf)
            return (lf, rf);
        // If either is double, promote both to double
        if (left is double || right is double)
            return (left is BigInteger lbi ? (double)lbi : Convert.ToDouble(left),
                    right is BigInteger rbi ? (double)rbi : Convert.ToDouble(right));
        // If either is float (no double), promote both to float
        if (left is float || right is float)
            return (left is BigInteger lbi2 ? (float)lbi2 : Convert.ToSingle(left),
                    right is BigInteger rbi2 ? (float)rbi2 : Convert.ToSingle(right));
        // If either is decimal, promote both to decimal
        if (left is decimal || right is decimal)
            return (left is BigInteger lbi2 ? (decimal)lbi2 : Convert.ToDecimal(left),
                    right is BigInteger rbi2 ? (decimal)rbi2 : Convert.ToDecimal(right));
        // If either is BigInteger, promote both to BigInteger
        if (left is BigInteger || right is BigInteger)
            return (ToBigInteger(left), ToBigInteger(right));
        // Otherwise promote to long (handles int+long, int+int, long+long)
        return (Convert.ToInt64(left), Convert.ToInt64(right));
    }

    private static object? Add(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;
        try { return AddCore(left, right); }
        catch (ArgumentOutOfRangeException)
        { throw new XQueryRuntimeException("FODT0002", "Date/time overflow in addition"); }
    }

    private static object? AddCore(object left, object right)
    {

        // Date/time + duration arithmetic (new wrapper types)
        if (left is Xdm.XsDate xld && right is Xdm.YearMonthDuration ymd)
            return new Xdm.XsDate(xld.Date.AddMonths(ymd.TotalMonths), xld.Timezone);
        if (left is Xdm.YearMonthDuration ymd2 && right is Xdm.XsDate xrd)
            return new Xdm.XsDate(xrd.Date.AddMonths(ymd2.TotalMonths), xrd.Timezone);
        if (left is Xdm.XsDate xld2 && right is TimeSpan ts)
        {
            // Per F&O §10.6.1: promote date to dateTime (midnight), add duration, extract date
            var dto = new DateTimeOffset(xld2.Date.ToDateTime(TimeOnly.MinValue), xld2.Timezone ?? TimeSpan.Zero);
            var result = dto.Add(ts);
            return new Xdm.XsDate(DateOnly.FromDateTime(result.DateTime), xld2.Timezone);
        }
        if (left is TimeSpan ts2 && right is Xdm.XsDate xrd2)
        {
            var dto = new DateTimeOffset(xrd2.Date.ToDateTime(TimeOnly.MinValue), xrd2.Timezone ?? TimeSpan.Zero);
            var result = dto.Add(ts2);
            return new Xdm.XsDate(DateOnly.FromDateTime(result.DateTime), xrd2.Timezone);
        }
        if (left is Xdm.XsDateTime xldt && right is Xdm.YearMonthDuration ymd3)
            return new Xdm.XsDateTime(xldt.Value.AddMonths(ymd3.TotalMonths), xldt.HasTimezone);
        if (left is Xdm.YearMonthDuration ymd4 && right is Xdm.XsDateTime xrdt)
            return new Xdm.XsDateTime(xrdt.Value.AddMonths(ymd4.TotalMonths), xrdt.HasTimezone);
        if (left is Xdm.XsDateTime xldt2 && right is TimeSpan ts3)
            return new Xdm.XsDateTime(xldt2.Value.Add(ts3), xldt2.HasTimezone);
        if (left is TimeSpan ts4 && right is Xdm.XsDateTime xrdt2)
            return new Xdm.XsDateTime(xrdt2.Value.Add(ts4), xrdt2.HasTimezone);
        if (left is Xdm.XsTime xlt && right is TimeSpan ts5)
        {
            var total = xlt.Time.ToTimeSpan() + ts5;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new Xdm.XsTime(new TimeOnly(ticks), xlt.Timezone, (int)(ticks % TimeSpan.TicksPerSecond));
        }
        if (left is TimeSpan ts6 && right is Xdm.XsTime xrt)
        {
            var total = xrt.Time.ToTimeSpan() + ts6;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new Xdm.XsTime(new TimeOnly(ticks), xrt.Timezone, (int)(ticks % TimeSpan.TicksPerSecond));
        }
        // Date/time + duration arithmetic (legacy raw types)
        if (left is DateOnly ld && right is Xdm.YearMonthDuration ymd0)
            return ld.AddMonths(ymd0.TotalMonths);
        if (left is Xdm.YearMonthDuration ymd02 && right is DateOnly rd)
            return rd.AddMonths(ymd02.TotalMonths);
        if (left is DateOnly ld2 && right is TimeSpan ts0)
            return ld2.AddDays((int)ts0.TotalDays);
        if (left is TimeSpan ts02 && right is DateOnly rd2)
            return rd2.AddDays((int)ts02.TotalDays);
        if (left is DateTimeOffset ldt && right is Xdm.YearMonthDuration ymd03)
            return ldt.AddMonths(ymd03.TotalMonths);
        if (left is Xdm.YearMonthDuration ymd04 && right is DateTimeOffset rdt)
            return rdt.AddMonths(ymd04.TotalMonths);
        if (left is DateTimeOffset ldt2 && right is TimeSpan ts03)
            return ldt2.Add(ts03);
        if (left is TimeSpan ts04 && right is DateTimeOffset rdt2)
            return rdt2.Add(ts04);
        // Time + dayTimeDuration
        if (left is TimeOnly lt && right is TimeSpan ts05)
        {
            var total = lt.ToTimeSpan() + ts05;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new TimeOnly(ticks);
        }
        if (left is TimeSpan ts06 && right is TimeOnly rt)
        {
            var total = rt.ToTimeSpan() + ts06;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new TimeOnly(ticks);
        }
        // Duration + duration
        if (left is Xdm.YearMonthDuration ya && right is Xdm.YearMonthDuration yb)
            return ya + yb;
        if (left is TimeSpan tsa && right is TimeSpan tsb)
            return tsa + tsb;

        if (IsNumeric(left) && IsNumeric(right))
        {
            var (l, r) = PromoteNumeric(left, right);
            return (l, r) switch
            {
                (long a, long b) => LongAddOrPromote(a, b),
                (BigInteger a, BigInteger b) => a + b,
                (float a, float b) => a + b,
                (double a, double b) => a + b,
                (decimal a, decimal b) => a + b,
                _ => Convert.ToDouble(l) + Convert.ToDouble(r)
            };
        }
        return ToDouble(left) + ToDouble(right);
    }

    private static object? Subtract(object? left, object? right)
    {
        try { return SubtractCore(left, right); }
        catch (ArgumentOutOfRangeException)
        { throw new XQueryRuntimeException("FODT0002", "Date/time overflow in subtraction"); }
    }

    private static object? SubtractCore(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;

        // Date/time - duration arithmetic (new wrapper types)
        if (left is Xdm.XsDate xld && right is Xdm.YearMonthDuration ymd)
            return new Xdm.XsDate(xld.Date.AddMonths(-ymd.TotalMonths), xld.Timezone);
        if (left is Xdm.XsDate xld2 && right is TimeSpan ts)
        {
            // Per F&O §10.6.5: promote date to dateTime (midnight), subtract duration, extract date
            var dto = new DateTimeOffset(xld2.Date.ToDateTime(TimeOnly.MinValue), xld2.Timezone ?? TimeSpan.Zero);
            var result = dto.Subtract(ts);
            return new Xdm.XsDate(DateOnly.FromDateTime(result.DateTime), xld2.Timezone);
        }
        if (left is Xdm.XsDateTime xldt && right is Xdm.YearMonthDuration ymd2)
            return new Xdm.XsDateTime(xldt.Value.AddMonths(-ymd2.TotalMonths), xldt.HasTimezone);
        if (left is Xdm.XsDateTime xldt2 && right is TimeSpan ts2)
            return new Xdm.XsDateTime(xldt2.Value.Subtract(ts2), xldt2.HasTimezone);
        if (left is Xdm.XsTime xlt && right is TimeSpan ts3)
        {
            var total = xlt.Time.ToTimeSpan() - ts3;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new Xdm.XsTime(new TimeOnly(ticks), xlt.Timezone, (int)(ticks % TimeSpan.TicksPerSecond));
        }
        if (left is Xdm.XsTime xlt2 && right is Xdm.XsTime xrt)
        {
            // Per XPath F&O §10.6.7: normalize both times to UTC before subtracting.
            // If either has no timezone, use the implicit timezone.
            var implicitTz = DateTimeOffset.Now.Offset;
            var leftTz = xlt2.Timezone ?? implicitTz;
            var rightTz = xrt.Timezone ?? implicitTz;
            var leftUtcTicks = xlt2.Time.Ticks - leftTz.Ticks;
            var rightUtcTicks = xrt.Time.Ticks - rightTz.Ticks;
            return TimeSpan.FromTicks(leftUtcTicks - rightUtcTicks);
        }
        if (left is Xdm.XsDate xld3 && right is Xdm.XsDate xrd)
        {
            // Per XPath F&O §10.6.5: normalize both dates to UTC-equivalent before subtracting.
            var implicitTz = DateTimeOffset.Now.Offset;
            var leftTz = xld3.Timezone ?? implicitTz;
            var rightTz = xrd.Timezone ?? implicitTz;
            var leftDto = new DateTimeOffset(xld3.Date.ToDateTime(TimeOnly.MinValue), leftTz);
            var rightDto = new DateTimeOffset(xrd.Date.ToDateTime(TimeOnly.MinValue), rightTz);
            return leftDto.UtcDateTime - rightDto.UtcDateTime;
        }
        if (left is Xdm.XsDateTime xldt3 && right is Xdm.XsDateTime xrdt)
        {
            // Per XPath F&O §10.6.6: normalize both dateTimes to UTC before subtracting.
            // If either has no timezone, use the implicit timezone.
            var implicitTz = DateTimeOffset.Now.Offset;
            var leftVal = xldt3.HasTimezone ? xldt3.Value : new DateTimeOffset(xldt3.Value.DateTime, implicitTz);
            var rightVal = xrdt.HasTimezone ? xrdt.Value : new DateTimeOffset(xrdt.Value.DateTime, implicitTz);
            return leftVal - rightVal;
        }
        // Mixed XsDateTime / DateTimeOffset subtraction
        if (left is Xdm.XsDateTime xldt4 && right is DateTimeOffset rDto)
            return xldt4.Value - rDto;
        if (left is DateTimeOffset lDto && right is Xdm.XsDateTime xrdt2)
            return lDto - xrdt2.Value;
        if (left is DateTimeOffset lDto2 && right is DateTimeOffset rDto2)
            return lDto2 - rDto2;
        // Date/time - duration arithmetic (legacy raw types)
        if (left is DateOnly ld && right is Xdm.YearMonthDuration ymd0)
            return ld.AddMonths(-ymd0.TotalMonths);
        if (left is DateOnly ld2 && right is TimeSpan ts0)
            return ld2.AddDays(-(int)ts0.TotalDays);
        if (left is DateTimeOffset ldt && right is Xdm.YearMonthDuration ymd02)
            return ldt.AddMonths(-ymd02.TotalMonths);
        if (left is DateTimeOffset ldt2 && right is TimeSpan ts02)
            return ldt2.Subtract(ts02);
        if (left is TimeOnly lt && right is TimeSpan ts03)
        {
            var total = lt.ToTimeSpan() - ts03;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new TimeOnly(ticks);
        }
        if (left is TimeOnly lt2 && right is TimeOnly rt)
            return lt2.ToTimeSpan() - rt.ToTimeSpan();
        if (left is DateOnly ld3 && right is DateOnly rd)
            return ld3.ToDateTime(TimeOnly.MinValue) - rd.ToDateTime(TimeOnly.MinValue);
        if (left is DateTimeOffset ldt3 && right is DateTimeOffset rdt)
            return ldt3 - rdt;
        // Duration - duration
        if (left is Xdm.YearMonthDuration ya && right is Xdm.YearMonthDuration yb)
            return ya - yb;
        if (left is TimeSpan tsa && right is TimeSpan tsb)
            return tsa - tsb;

        if (IsNumeric(left) && IsNumeric(right))
        {
            var (l, r) = PromoteNumeric(left, right);
            return (l, r) switch
            {
                (long a, long b) => LongSubtractOrPromote(a, b),
                (BigInteger a, BigInteger b) => a - b,
                (float a, float b) => a - b,
                (double a, double b) => a - b,
                (decimal a, decimal b) => a - b,
                _ => Convert.ToDouble(l) - Convert.ToDouble(r)
            };
        }
        return ToDouble(left) - ToDouble(right);
    }

    private static object? Multiply(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;

        // Duration * number and number * duration
        if (left is Xdm.YearMonthDuration ymd && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot multiply duration by NaN");
            if (double.IsInfinity(d)) throw new XQueryRuntimeException("FODT0002", "Duration overflow");
            return ymd * d;
        }
        if (IsNumeric(left) && right is Xdm.YearMonthDuration ymd2)
        {
            var d = ToDouble(left);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot multiply duration by NaN");
            if (double.IsInfinity(d)) throw new XQueryRuntimeException("FODT0002", "Duration overflow");
            return ymd2 * d;
        }
        if (left is TimeSpan ts && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot multiply duration by NaN");
            if (double.IsInfinity(d)) throw new XQueryRuntimeException("FODT0002", "Duration overflow");
            return TimeSpan.FromTicks((long)(ts.Ticks * d));
        }
        if (IsNumeric(left) && right is TimeSpan ts2)
        {
            var d = ToDouble(left);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot multiply duration by NaN");
            if (double.IsInfinity(d)) throw new XQueryRuntimeException("FODT0002", "Duration overflow");
            return TimeSpan.FromTicks((long)(ts2.Ticks * d));
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            var (l, r) = PromoteNumeric(left, right);
            return (l, r) switch
            {
                (long a, long b) => LongMultiplyOrPromote(a, b),
                (BigInteger a, BigInteger b) => a * b,
                (float a, float b) => a * b,
                (double a, double b) => a * b,
                (decimal a, decimal b) => DecimalMultiplyOrPromote(a, b),
                _ => Convert.ToDouble(l) * Convert.ToDouble(r)
            };
        }
        return ToDouble(left) * ToDouble(right);
    }

    // Overflow-safe integer arithmetic: promotes to BigInteger when result exceeds Int64 range.
    private static object LongAddOrPromote(long a, long b)
    {
        try
        { return checked(a + b); }
        catch (OverflowException)
        {
            var result = (BigInteger)a + b;
            if (result.GetByteCount() > 1_000_000)
                throw new XQueryRuntimeException("FOAR0002", "Numeric result exceeds maximum supported size");
            return result;
        }
    }

    private static object LongSubtractOrPromote(long a, long b)
    {
        try
        { return checked(a - b); }
        catch (OverflowException)
        {
            var result = (BigInteger)a - b;
            if (result.GetByteCount() > 1_000_000)
                throw new XQueryRuntimeException("FOAR0002", "Numeric result exceeds maximum supported size");
            return result;
        }
    }

    private static object LongMultiplyOrPromote(long a, long b)
    {
        try
        { return checked(a * b); }
        catch (OverflowException)
        {
            var result = (BigInteger)a * b;
            if (result.GetByteCount() > 1_000_000)
                throw new XQueryRuntimeException("FOAR0002", "Numeric result exceeds maximum supported size");
            return result;
        }
    }

    private static object DecimalMultiplyOrPromote(decimal a, decimal b)
    {
        try
        { return a * b; }
        catch (OverflowException) { return (double)a * (double)b; }
    }

    private static object? Divide(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;
        if (left is string || right is string)
            throw new XQueryRuntimeException("XPTY0004",
                "Arithmetic operator 'div' is not defined for xs:string");

        // Duration / number
        if (left is Xdm.YearMonthDuration ymd && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot divide duration by NaN");
            if (d == 0) throw new XQueryRuntimeException("FODT0002", "Duration division by zero");
            return new Xdm.YearMonthDuration((int)Math.Floor(ymd.TotalMonths / d + 0.5));
        }
        if (left is TimeSpan ts && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot divide duration by NaN");
            if (d == 0) throw new XQueryRuntimeException("FODT0002", "Duration division by zero");
            return TimeSpan.FromTicks((long)(ts.Ticks / d));
        }
        // Duration / duration = decimal
        if (left is Xdm.YearMonthDuration ya && right is Xdm.YearMonthDuration yb)
        {
            if (yb.TotalMonths == 0)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
            return (decimal)ya.TotalMonths / yb.TotalMonths;
        }
        if (left is TimeSpan tsa && right is TimeSpan tsb)
        {
            if (tsb.Ticks == 0)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
            return (decimal)tsa.Ticks / tsb.Ticks;
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            // XQuery: integer div integer = decimal
            if (left is int or long or BigInteger && right is int or long or BigInteger)
            {
                var ld = ToBigInteger(left);
                var rd = ToBigInteger(right);
                if (rd.IsZero)
                    throw new XQueryRuntimeException("FOAR0001", "Division by zero");
                // Use decimal when values fit, otherwise use double
                try
                {
                    return (decimal)ld / (decimal)rd;
                }
                catch (OverflowException)
                {
                    return (double)ld / (double)rd;
                }
            }
            var (l, r) = PromoteNumeric(left, right);
            return (l, r) switch
            {
                (decimal a, decimal b) when b != 0 => a / b,
                (decimal _, decimal _) => throw new XQueryRuntimeException("FOAR0001", "Division by zero"),
                (float a, float b) => a / b,
                (double a, double b) => a / b,
                _ => Convert.ToDouble(l) / Convert.ToDouble(r)
            };
        }
        return ToDouble(left) / ToDouble(right);
    }

    private static object? IntegerDivide(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;

        left = QueryExecutionContext.Atomize(left);
        right = QueryExecutionContext.Atomize(right);
        // Float/double special cases
        if ((left is float lf && float.IsNaN(lf)) || (left is double ld2 && double.IsNaN(ld2)))
            throw new XQueryRuntimeException("FOAR0002", "Invalid argument for integer division: NaN");
        if ((left is float lf2 && float.IsInfinity(lf2)) || (left is double ld3 && double.IsInfinity(ld3)))
            throw new XQueryRuntimeException("FOAR0002", "Invalid argument for integer division: Infinity");
        if ((right is float rf && float.IsNaN(rf)) || (right is double rd2 && double.IsNaN(rd2)))
            throw new XQueryRuntimeException("FOAR0002", "Invalid argument for integer division: NaN");
        // x idiv INF = 0 when x is finite
        if ((right is float rf2 && float.IsInfinity(rf2)) || (right is double rd3 && double.IsInfinity(rd3)))
            return 0L;
        // BigInteger idiv
        if (left is BigInteger || right is BigInteger)
        {
            var lb = ToBigInteger(left);
            var rb = ToBigInteger(right);
            if (rb.IsZero)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
            var result = BigInteger.Divide(lb, rb);
            // Narrow to long if possible
            if (result >= long.MinValue && result <= long.MaxValue)
                return (long)result;
            return result;
        }
        // Per XPath spec: A idiv B = truncate(A div B)
        // Must perform real division first, then truncate — not convert to int first
        if (left is float or double || right is float or double)
        {
            var dl = Convert.ToDouble(left is string sl ? ToDouble(sl) : left);
            var dr = Convert.ToDouble(right is string sr ? ToDouble(sr) : right);
            if (dr == 0)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
            var quotient = Math.Truncate(dl / dr);
            if (double.IsInfinity(quotient) || double.IsNaN(quotient)
                || quotient > (double)long.MaxValue || quotient < (double)long.MinValue)
                throw new XQueryRuntimeException("FOAR0002",
                    "Integer overflow in integer division");
            return (long)quotient;
        }
        if (left is decimal or int or long || right is decimal)
        {
            try
            {
                var dl = Convert.ToDecimal(left is string sl2 ? ToDouble(sl2) : left);
                var dr = Convert.ToDecimal(right is string sr2 ? ToDouble(sr2) : right);
                if (dr == 0)
                    throw new XQueryRuntimeException("FOAR0001", "Division by zero");
                return (long)Math.Truncate(dl / dr);
            }
            catch (OverflowException)
            {
                throw new XQueryRuntimeException("FOAR0002",
                    "Integer overflow in integer division");
            }
        }
        var l = Convert.ToInt64(left is string sl3 ? ToDouble(sl3) : left);
        var r = Convert.ToInt64(right is string sr3 ? ToDouble(sr3) : right);
        if (r == 0)
            throw new XQueryRuntimeException("FOAR0001", "Division by zero");
        return l / r;
    }

    private static object? Modulo(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;

        // Use PromoteNumeric for correct type promotion (float/double/decimal/untypedAtomic)
        var (pl, pr) = PromoteNumeric(left, right);

        return (pl, pr) switch
        {
            (float lf, float rf) => lf % rf, // IEEE 754: x mod 0 = NaN
            (double ld, double rd) => ld % rd, // IEEE 754: x mod 0 = NaN
            (decimal lm, decimal rm) when rm != 0 => lm % rm,
            (decimal _, decimal _) => throw new XQueryRuntimeException("FOAR0001", "Division by zero"),
            (BigInteger lb, BigInteger rb) when !rb.IsZero =>
                BigInteger.Remainder(lb, rb) is var r && r >= long.MinValue && r <= long.MaxValue ? (long)r : r,
            (BigInteger _, BigInteger _) => throw new XQueryRuntimeException("FOAR0001", "Division by zero"),
            (long ll, long rl) when rl != 0 => ll % rl,
            (long _, long _) => throw new XQueryRuntimeException("FOAR0001", "Division by zero"),
            _ => Convert.ToDouble(pl) % Convert.ToDouble(pr)
        };
    }

    private static bool IsNumeric(object? v) =>
        v is int or long or double or float or decimal or BigInteger;

    private static bool IsDurationType(object? v) =>
        v is Xdm.XsDuration or Xdm.YearMonthDuration or TimeSpan;

    /// <summary>
    /// Cross-type duration equality for eq/ne.
    /// xs:duration, xs:yearMonthDuration, and xs:dayTimeDuration are all comparable via eq/ne.
    /// </summary>
    private static bool DurationValueEqual(object left, object right)
    {
        var (lMonths, lTicks) = GetDurationComponents(left);
        var (rMonths, rTicks) = GetDurationComponents(right);
        return lMonths == rMonths && lTicks == rTicks;
    }

    private static (int months, long ticks) GetDurationComponents(object dur) => dur switch
    {
        Xdm.XsDuration d => (d.TotalMonths, d.DayTime.Ticks),
        Xdm.YearMonthDuration ymd => (ymd.TotalMonths, 0),
        TimeSpan ts => (0, ts.Ticks),
        _ => (0, 0)
    };

    private static BigInteger ToBigInteger(object? v) => v switch
    {
        BigInteger bi => bi,
        long l => l,
        int i => i,
        decimal m => (BigInteger)m,
        double d => (BigInteger)d,
        float f => (BigInteger)f,
        _ => (BigInteger)Convert.ToInt64(v)
    };

    private static bool IsComparisonOperator(BinaryOperator op) => op is
        BinaryOperator.Equal or BinaryOperator.NotEqual or
        BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual or
        BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
        BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual or
        BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
        BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual;

    private static double ToDouble(object? v)
    {
        v = QueryExecutionContext.Atomize(v);
        if (v is string s)
            return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        if (v is BigInteger bi)
            return (double)bi;
        return Convert.ToDouble(v);
    }

    private static float ToFloat(object? v)
    {
        v = QueryExecutionContext.Atomize(v);
        if (v is float f) return f;
        if (v is string s)
            return float.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var fp) ? fp : float.NaN;
        if (v is BigInteger bi)
            return (float)bi;
        return Convert.ToSingle(v);
    }

    /// <summary>
    /// Converts to double, throwing FORG0001 if a string value cannot be cast.
    /// Used in XPath 2.0+ general comparisons where untypedAtomic→double cast failure is an error.
    /// </summary>
    private static double ToDoubleOrThrow(object? v)
    {
        v = QueryExecutionContext.Atomize(v);
        if (v is string s)
        {
            s = s.Trim();
            if (s == "INF")
                return double.PositiveInfinity;
            if (s == "-INF")
                return double.NegativeInfinity;
            if (s == "NaN")
                return double.NaN;
            if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
            throw new Functions.XQueryException("FORG0001", $"Cannot cast '{s}' to xs:double");
        }
        if (v is BigInteger bi)
            return (double)bi;
        return Convert.ToDouble(v);
    }

    private static bool ValueEquals(object? left, object? right, StringComparison stringComparison = StringComparison.Ordinal)
    {
        // Atomize XDM nodes before comparison
        left = QueryExecutionContext.Atomize(left);
        right = QueryExecutionContext.Atomize(right);

        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;

        // XPath general comparison: if one operand is xs:untypedAtomic (string) and
        // the other is numeric, cast the string to xs:double
        var leftIsString = left is string;
        var rightIsString = right is string;
        var leftIsNumeric = IsNumeric(left);
        var rightIsNumeric = IsNumeric(right);

        if ((leftIsString && rightIsNumeric) || (rightIsString && leftIsNumeric))
        {
            var ld = ToDoubleOrThrow(left);
            var rd = ToDoubleOrThrow(right);
            // NaN != NaN per XQuery/IEEE 754
            if (double.IsNaN(ld) || double.IsNaN(rd))
                return false;
            return ld == rd;
        }

        // Both numeric — promote and compare
        if (leftIsNumeric && rightIsNumeric)
        {
            // If either is double, compare as double.
            if (left is double || right is double)
            {
                var ld = ToDouble(left);
                var rd = ToDouble(right);
                // NaN != NaN per XQuery/IEEE 754
                if (double.IsNaN(ld) || double.IsNaN(rd))
                    return false;
                return ld == rd;
            }
            // Otherwise, if either is xs:float (but neither is double), numeric promotion
            // promotes the other operand to xs:float and compares as float (QT3 K-SeqExprCast-81).
            if (left is float || right is float)
            {
                var lf = ToFloat(left);
                var rf = ToFloat(right);
                if (float.IsNaN(lf) || float.IsNaN(rf))
                    return false;
                return lf == rf;
            }
            // BigInteger comparison
            if (left is BigInteger || right is BigInteger)
                return ToBigInteger(left) == ToBigInteger(right);
            // Both are integer or decimal — compare as decimal for precision
            return Convert.ToDecimal(left) == Convert.ToDecimal(right);
        }

        // Boolean comparison
        if (left is bool lb && right is bool rb)
            return lb == rb;

        // QName comparison — namespace URI + local name per XPath spec
        // QName can only be compared with QName; other types are XPTY0004
        if (left is QName || right is QName)
        {
            if (left is QName lq && right is QName rq)
            {
                if (lq.LocalName != rq.LocalName)
                    return false;
                var lUri = lq.ResolvedNamespace;
                var rUri = rq.ResolvedNamespace;
                if (lUri != null && rUri != null)
                    return lUri == rUri;
                return lq.Namespace == rq.Namespace;
            }
            throw new Functions.XQueryException("XPTY0004",
                "Cannot compare xs:QName with a non-QName value");
        }

        // Date/time comparison — normalize to UTC before comparing
        if (left is Xdm.XsDateTime ldt && right is Xdm.XsDateTime rdt)
            return ldt.CompareTo(rdt) == 0;
        if (left is Xdm.XsDate ld2 && right is Xdm.XsDate rd2)
            return ld2.CompareTo(rd2) == 0;
        if (left is Xdm.XsTime lt && right is Xdm.XsTime rt)
            return lt.CompareTo(rt) == 0;

        // Duration comparison — cross-type equality for eq/ne
        if (IsDurationType(left) && IsDurationType(right))
            return DurationValueEqual(left, right);

        // Binary comparison — compare underlying byte arrays
        if (left is Xdm.XdmValue lv && right is Xdm.XdmValue rv
            && lv.Type == rv.Type
            && lv.RawValue is byte[] lBytes && rv.RawValue is byte[] rBytes)
            return lBytes.AsSpan().SequenceEqual(rBytes);

        // Gregorian date component types: convert to reference xs:dateTime and compare.
        // Per F&O 4.0 op:gDay-equal etc., the operand's timezone (explicit or implicit)
        // applies to the reference dateTime, so values differing only in timezone but
        // representing the same UTC instant are equal.
        if (left is Xdm.XsGDay lgd && right is Xdm.XsGDay rgd)
            return GregorianToUtcTicks(lgd.Value, GregorianKind.GDay)
                == GregorianToUtcTicks(rgd.Value, GregorianKind.GDay);
        if (left is Xdm.XsGMonth lgm && right is Xdm.XsGMonth rgm)
            return GregorianToUtcTicks(lgm.Value, GregorianKind.GMonth)
                == GregorianToUtcTicks(rgm.Value, GregorianKind.GMonth);
        if (left is Xdm.XsGYear lgy && right is Xdm.XsGYear rgy)
            return GregorianToUtcTicks(lgy.Value, GregorianKind.GYear)
                == GregorianToUtcTicks(rgy.Value, GregorianKind.GYear);
        if (left is Xdm.XsGYearMonth lgym && right is Xdm.XsGYearMonth rgym)
            return GregorianToUtcTicks(lgym.Value, GregorianKind.GYearMonth)
                == GregorianToUtcTicks(rgym.Value, GregorianKind.GYearMonth);
        if (left is Xdm.XsGMonthDay lgmd && right is Xdm.XsGMonthDay rgmd)
            return GregorianToUtcTicks(lgmd.Value, GregorianKind.GMonthDay)
                == GregorianToUtcTicks(rgmd.Value, GregorianKind.GMonthDay);

        // String comparison using XPath canonical string representations
        return string.Equals(
            Functions.ConcatFunction.XQueryStringValue(left),
            Functions.ConcatFunction.XQueryStringValue(right),
            stringComparison);
    }

    private enum GregorianKind { GYear, GMonth, GDay, GYearMonth, GMonthDay }

    /// <summary>
    /// Converts a Gregorian date component lexical value to UTC ticks using the reference
    /// xs:dateTime (1972-12-31T00:00:00 for gYear/gMonth/gDay/gYearMonth, where the year
    /// is copied from gYear/gYearMonth and month from gMonth/gYearMonth/gMonthDay and day
    /// from gDay/gMonthDay; missing components default to the reference 1972-12-31).
    /// The operand's timezone (explicit or implicit) is applied to shift to UTC.
    /// </summary>
    private static long GregorianToUtcTicks(string lex, GregorianKind kind)
    {
        // Strip and parse timezone suffix (Z | ±HH:MM). Empty => no timezone (use implicit).
        TimeSpan? tz = null;
        string body = lex;
        if (body.EndsWith('Z'))
        {
            tz = TimeSpan.Zero;
            body = body[..^1];
        }
        else
        {
            // Look for +HH:MM or -HH:MM at the end
            int len = body.Length;
            if (len >= 6 && body[len - 3] == ':' && (body[len - 6] == '+' || body[len - 6] == '-'))
            {
                var sign = body[len - 6] == '+' ? 1 : -1;
                if (int.TryParse(body.AsSpan(len - 5, 2), out var hh)
                    && int.TryParse(body.AsSpan(len - 2, 2), out var mm))
                {
                    tz = TimeSpan.FromMinutes(sign * (hh * 60 + mm));
                    body = body[..(len - 6)];
                }
            }
        }

        int year = 1972, month = 12, day = 31;
        switch (kind)
        {
            case GregorianKind.GYear:
                // Lexical form: [-]YYYY[+-]HH:MM or [-]YYYY Z
                _ = int.TryParse(body, out year);
                break;
            case GregorianKind.GMonth:
                // Lexical form: --MM
                if (body.StartsWith("--", StringComparison.Ordinal) && body.Length >= 4)
                    _ = int.TryParse(body.AsSpan(2, 2), out month);
                break;
            case GregorianKind.GDay:
                // Lexical form: ---DD
                if (body.StartsWith("---", StringComparison.Ordinal) && body.Length >= 5)
                    _ = int.TryParse(body.AsSpan(3, 2), out day);
                break;
            case GregorianKind.GYearMonth:
                // Lexical form: [-]YYYY-MM
                {
                    var dash = body.LastIndexOf('-');
                    if (dash > 0 && dash < body.Length - 1)
                    {
                        _ = int.TryParse(body.AsSpan(0, dash), out year);
                        _ = int.TryParse(body.AsSpan(dash + 1), out month);
                    }
                }
                break;
            case GregorianKind.GMonthDay:
                // Lexical form: --MM-DD
                if (body.StartsWith("--", StringComparison.Ordinal) && body.Length >= 7)
                {
                    _ = int.TryParse(body.AsSpan(2, 2), out month);
                    _ = int.TryParse(body.AsSpan(5, 2), out day);
                }
                break;
        }

        // Clamp day to valid range for the month (e.g., Feb 29 in leap year)
        var daysInMonth = DateTime.DaysInMonth(Math.Abs(year), Math.Max(1, Math.Min(12, month)));
        day = Math.Max(1, Math.Min(day, daysInMonth));
        month = Math.Max(1, Math.Min(12, month));

        // Build local DateTime and apply timezone offset to get UTC ticks
        var local = new DateTime(Math.Max(1, Math.Abs(year)), month, day, 0, 0, 0, DateTimeKind.Unspecified);
        var offset = tz ?? TimeSpan.Zero; // implicit = UTC
        var utc = local - offset;
        return utc.Ticks;
    }

    private static int ValueCompare(object? left, object? right, StringComparison stringComparison = StringComparison.Ordinal)
    {
        // Atomize XDM nodes before comparison
        left = QueryExecutionContext.Atomize(left);
        right = QueryExecutionContext.Atomize(right);

        // Unwrap XsTypedString to plain string
        if (left is Xdm.XsTypedString tsLeft) left = tsLeft.Value;
        if (right is Xdm.XsTypedString tsRight) right = tsRight.Value;

        if (left is null && right is null)
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        // XPath general comparison: if one operand is xs:untypedAtomic (string) and
        // the other is numeric, cast the string to xs:double
        var leftIsString = left is string;
        var rightIsString = right is string;
        var leftIsNumeric = IsNumeric(left);
        var rightIsNumeric = IsNumeric(right);

        if ((leftIsString && rightIsNumeric) || (rightIsString && leftIsNumeric))
        {
            var ld = ToDoubleOrThrow(left);
            var rd = ToDoubleOrThrow(right);
            return ld.CompareTo(rd);
        }

        // Both numeric — promote and compare
        if (leftIsNumeric && rightIsNumeric)
        {
            if (left is double or float || right is double or float)
            {
                var ld = ToDouble(left);
                var rd = ToDouble(right);
                return ld.CompareTo(rd);
            }
            if (left is BigInteger || right is BigInteger)
                return ToBigInteger(left).CompareTo(ToBigInteger(right));
            return Convert.ToDecimal(left).CompareTo(Convert.ToDecimal(right));
        }

        // Boolean comparison
        if (left is bool lb && right is bool rb)
            return lb.CompareTo(rb);

        // Date/time comparison — normalize to UTC before comparing
        if (left is Xdm.XsDateTime ldt && right is Xdm.XsDateTime rdt)
            return ldt.CompareTo(rdt);
        if (left is Xdm.XsDate ld2 && right is Xdm.XsDate rd2)
            return ld2.CompareTo(rd2);
        if (left is Xdm.XsTime lt && right is Xdm.XsTime rt)
            return lt.CompareTo(rt);

        // Duration comparison — yearMonthDuration and dayTimeDuration support ordering
        // but only within the same subtype. Cross-type (YMD vs DTD) and xs:duration are not ordered.
        if (left is Xdm.YearMonthDuration lym && right is Xdm.YearMonthDuration rym)
            return lym.CompareTo(rym);
        if (left is TimeSpan lts && right is TimeSpan rts)
            return lts.CompareTo(rts);
        // xs:duration is only eq/ne, not ordered
        if (IsDurationType(left) && IsDurationType(right))
            throw new Functions.XQueryException("XPTY0004",
                "Duration values are not ordered — cannot use lt, gt, le, ge for xs:duration or cross-type duration comparison");

        // QName only supports eq/ne, not ordering
        if (left is QName || right is QName)
            throw new Functions.XQueryException("XPTY0004",
                "Values of type xs:QName are not ordered — cannot use lt, gt, le, ge");

        // Gregorian date component types only support eq/ne, not ordering
        if (left is Xdm.XsGYear or Xdm.XsGMonth or Xdm.XsGDay or Xdm.XsGMonthDay or Xdm.XsGYearMonth ||
            right is Xdm.XsGYear or Xdm.XsGMonth or Xdm.XsGDay or Xdm.XsGMonthDay or Xdm.XsGYearMonth)
            throw new Functions.XQueryException("XPTY0004",
                "Gregorian date component types are not ordered — cannot use lt, gt, le, ge");

        // Binary comparison — octet-by-octet (unsigned) ordering of the underlying bytes.
        // Cross-type (hexBinary vs base64Binary) and binary vs string are XPTY0004.
        if (left is Xdm.XdmValue lbv && lbv.RawValue is byte[])
        {
            if (right is Xdm.XdmValue rbv && rbv.RawValue is byte[] rBytes && lbv.Type == rbv.Type)
                return ((byte[])lbv.RawValue!).AsSpan().SequenceCompareTo(rBytes);
            throw new Functions.XQueryException("XPTY0004",
                $"Cannot compare {lbv.Type} with {right.GetType().Name}");
        }
        if (right is Xdm.XdmValue rbv2 && rbv2.RawValue is byte[])
            throw new Functions.XQueryException("XPTY0004",
                $"Cannot compare {left.GetType().Name} with {rbv2.Type}");

        // String comparison using XPath canonical string representations
        // Use CollationHelper.CompareStrings for correct Unicode codepoint ordering
        // (.NET's string.Compare with Ordinal uses UTF-16 code unit ordering which
        // differs from codepoint ordering for supplementary characters)
        return Functions.CollationHelper.CompareStrings(
            Functions.ConcatFunction.XQueryStringValue(left),
            Functions.ConcatFunction.XQueryStringValue(right),
            stringComparison);
    }
}

/// <summary>
/// Unary operator node.
/// </summary>
public sealed class UnaryOperatorNode : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required UnaryOperator Operator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? value;
        bool hasValue;
        if (Operator == UnaryOperator.Not)
        {
            // not() needs the full sequence for correct EBV (FORG0006 on multi-atom sequences)
            var items = new List<object?>();
            await foreach (var item in Operand.ExecuteAsync(context))
                items.Add(item);
            value = items.Count switch
            {
                0 => null,
                1 => items[0],
                _ => items.ToArray()
            };
            hasValue = true; // not() of empty sequence = true (EBV rules)
        }
        else
        {
            value = null;
            hasValue = false;
            await foreach (var item in Operand.ExecuteAsync(context))
            {
                value = item;
                hasValue = true;
                break;
            }
        }

        // XPath 3.1 §4.2: unary +/- of empty sequence yields the empty sequence
        // (arithmetic operators return empty when any operand is empty, except in
        // XPath 1.0 backwards-compatible mode).
        if (!hasValue && !context.BackwardsCompatible
            && Operator is UnaryOperator.Plus or UnaryOperator.Minus)
        {
            yield break;
        }

        var result = Operator switch
        {
            UnaryOperator.Plus => CoerceToNumeric(value, context.BackwardsCompatible),
            UnaryOperator.Minus => Negate(value, context.BackwardsCompatible),
            UnaryOperator.Not => !QueryExecutionContext.EffectiveBooleanValue(value),
            _ => value
        };

        yield return result;
    }

    private static object? CoerceToNumeric(object? value, bool backwardsCompatible = false)
    {
        value = QueryExecutionContext.Atomize(value);
        if (value is null) return value;
        if (value is int or long or double or decimal or float or BigInteger) return value;
        if (value is Xdm.XsUntypedAtomic u)
            return double.TryParse(u.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d2) ? d2 : double.NaN;
        if (value is string s)
        {
            if (backwardsCompatible)
                return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
            throw new XQueryRuntimeException("XPTY0004", "Unary plus is not defined for xs:string");
        }
        return Convert.ToDouble(value);
    }

    private static object? Negate(object? value, bool backwardsCompatible = false)
    {
        value = QueryExecutionContext.Atomize(value);
        if (value is null) return null; // -() = ()
        // xs:untypedAtomic promotes to xs:double for arithmetic
        if (value is Xdm.XsUntypedAtomic u)
            value = double.TryParse(u.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var ud) ? ud : double.NaN;
        if (backwardsCompatible && value is string s)
            return -(double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN);
        if (value is string)
            throw new XQueryRuntimeException("XPTY0004", "Unary minus is not defined for xs:string");
        // In backward-compat mode (XPath 1.0), all numbers are doubles, so -(integer 0) = double -0.0
        if (backwardsCompatible)
        {
            var d = value is BigInteger bi2 ? (double)bi2 : Convert.ToDouble(value);
            return -d;
        }
        return value switch
        {
            int i => -i,
            long l => -l,
            BigInteger bi => -bi,
            float f => -f,
            double d => -d,
            decimal m => -m,
            _ => -Convert.ToDouble(value)
        };
    }
}

/// <summary>
/// If expression operator.
/// </summary>
public sealed class IfOperator : PhysicalOperator
{
    public required PhysicalOperator Condition { get; init; }
    public required PhysicalOperator Then { get; init; }
    public required PhysicalOperator Else { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? condValue = null;
        await foreach (var item in Condition.ExecuteAsync(context))
        {
            condValue = item;
            break;
        }

        var branch = QueryExecutionContext.EffectiveBooleanValue(condValue) ? Then : Else;

        await foreach (var item in branch.ExecuteAsync(context))
        {
            yield return item;
        }
    }
}

/// <summary>
/// Sequence operator.
/// </summary>
public sealed class SequenceOperator : PhysicalOperator
{
    public required IReadOnlyList<PhysicalOperator> Items { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        foreach (var item in Items)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await foreach (var result in item.ExecuteAsync(context))
            {
                yield return result;
            }
        }
    }
}

/// <summary>
/// Element constructor operator — creates an XdmElement node with attributes and content.
/// Used for both direct element constructors (&lt;elem&gt;...&lt;/elem&gt;) and computed
/// element constructors (element name { content }).
/// </summary>
public sealed class ElementConstructorOperator : PhysicalOperator
{
    public required QName Name { get; init; }
    public required IReadOnlyList<PhysicalOperator> AttributeOperators { get; init; }
    public required IReadOnlyList<PhysicalOperator> ContentOperators { get; init; }

    /// <summary>
    /// When true, this element constructor is a direct child of another element constructor
    /// (not inside an enclosed expression). Direct child constructors are NOT subject to
    /// copy-namespaces semantics because they define their own namespace declarations as part
    /// of the construction, rather than copying an existing node.
    /// </summary>
    public bool IsDirectChild { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeStore as INodeBuilder;
        if (store == null)
        {
            // Fallback: serialize as XML string when no INodeBuilder is available
            yield return await SerializeAsString(context);
            yield break;
        }

        // Resolve namespace: either use the static analysis ID or intern a new one from the URI
        // Use a local copy of the element name so prefix renaming (for namespace conflicts)
        // can update it without mutating the operator.
        var elemName = Name;
        var nsId = elemName.Namespace;
        if (elemName.ResolvedNamespace != null)
        {
            if (nsId == NamespaceId.None)
                nsId = store.InternNamespace(elemName.ResolvedNamespace);
            // Register the namespace ID → URI mapping for serialization (reverse lookup)
            if (store is XdmDocumentStore docStore)
                docStore.RegisterNamespace(elemName.ResolvedNamespace, nsId);
        }

        // Evaluate attributes
        var attrIds = new List<NodeId>();
        var elemId = store.AllocateId();
        var constructedDocId = new DocumentId(0);

        // Track attribute (namespace, localName) for XQDY0025 duplicate detection
        var seenAttrs = new HashSet<(NamespaceId, string)>();
        void CheckDuplicateAttr(XdmAttribute a)
        {
            if (!seenAttrs.Add((a.Namespace, a.LocalName)))
                throw new XQueryRuntimeException("XQDY0025",
                    $"Duplicate attribute in element constructor: {a.LocalName}");
            if (a.Prefix == "xml")
            {
                if (a.LocalName == "base")
                {
                    // FORG0001: malformed percent-escape in xml:base URI
                    var bv = a.Value ?? string.Empty;
                    for (int i = 0; i < bv.Length; i++)
                    {
                        if (bv[i] == '%')
                        {
                            if (i + 2 >= bv.Length || !Uri.IsHexDigit(bv[i + 1]) || !Uri.IsHexDigit(bv[i + 2]))
                                throw new XQueryRuntimeException("FORG0001",
                                    $"Invalid xml:base URI: '{bv}'");
                            i += 2;
                        }
                    }
                }
                if (a.LocalName == "id")
                {
                    // XQDY0091: xml:id value must be a valid NCName after whitespace normalization
                    var norm = System.Text.RegularExpressions.Regex.Replace((a.Value ?? string.Empty).Trim(), "\\s+", " ");
                    if (norm.Length == 0 || !System.Xml.XmlConvert.IsNCNameChar(norm[0]) || norm.Any(c => !System.Xml.XmlConvert.IsNCNameChar(c)))
                        throw new XQueryRuntimeException("XQDY0091", $"Invalid xml:id value: '{a.Value}'");
                }
                else if (a.LocalName == "space")
                {
                    // XQDY0092: xml:space must be 'preserve' or 'default'
                    var v = a.Value ?? string.Empty;
                    if (v != "preserve" && v != "default")
                        throw new XQueryRuntimeException("XQDY0092", $"xml:space value must be 'preserve' or 'default', got '{v}'");
                }
            }
        }

        foreach (var attrOp in AttributeOperators)
        {
            await foreach (var attrResult in attrOp.ExecuteAsync(context))
            {
                if (attrResult is XdmAttribute attr)
                {
                    CheckDuplicateAttr(attr);
                    // Deep-copy attribute into constructed tree with new parent
                    var newAttrId = store.AllocateId();
                    var newAttr = new XdmAttribute
                    {
                        Id = newAttrId,
                        Document = constructedDocId,
                        Namespace = attr.Namespace,
                        LocalName = attr.LocalName,
                        Prefix = attr.Prefix,
                        Value = attr.Value,
                        TypeAnnotation = attr.TypeAnnotation,
                        IsId = attr.IsId
                    };
                    newAttr.Parent = elemId;
                    store.RegisterNode(newAttr);
                    attrIds.Add(newAttrId);
                }
            }
        }

        // Propagate namespace bindings from this element's xmlns: attributes
        // to the execution context, so computed constructors in content can resolve prefixes.
        // Save old bindings to restore after content evaluation.
        var oldBindings = context.PrefixNamespaceBindings;
        var oldConstructorBindings = context.EnclosingConstructorBindings;
        Dictionary<string, string>? newBindings = null;
        // Separately, accumulate ONLY the bindings contributed by constructor syntax
        // (xmlns:* attrs + element's own prefix binding). Prolog declarations are NOT
        // added here — they only become part of an element's in-scope namespaces if used.
        Dictionary<string, string>? newConstructorBindings = null;
        foreach (var attrId in attrIds)
        {
            if (store.GetNode(attrId) is XdmAttribute nsAttr)
            {
                if (nsAttr.Prefix == "xmlns")
                {
                    newBindings ??= oldBindings != null
                        ? new Dictionary<string, string>(oldBindings)
                        : new Dictionary<string, string>();
                    newBindings[nsAttr.LocalName] = nsAttr.Value;
                    newConstructorBindings ??= oldConstructorBindings != null
                        ? new Dictionary<string, string>(oldConstructorBindings)
                        : new Dictionary<string, string>();
                    newConstructorBindings[nsAttr.LocalName] = nsAttr.Value;
                }
                else if (string.IsNullOrEmpty(nsAttr.Prefix) && nsAttr.LocalName == "xmlns")
                {
                    newBindings ??= oldBindings != null
                        ? new Dictionary<string, string>(oldBindings)
                        : new Dictionary<string, string>();
                    newBindings[""] = nsAttr.Value;
                    newConstructorBindings ??= oldConstructorBindings != null
                        ? new Dictionary<string, string>(oldConstructorBindings)
                        : new Dictionary<string, string>();
                    newConstructorBindings[""] = nsAttr.Value;
                }
            }
        }
        // Add the element's own prefix binding: `<foo:e>` makes `foo` in-scope for the element,
        // even if `foo` was only declared at the prolog level.
        if (!string.IsNullOrEmpty(elemName.Prefix) && elemName.ResolvedNamespace != null)
        {
            newConstructorBindings ??= oldConstructorBindings != null
                ? new Dictionary<string, string>(oldConstructorBindings)
                : new Dictionary<string, string>();
            if (!newConstructorBindings.ContainsKey(elemName.Prefix))
                newConstructorBindings[elemName.Prefix] = elemName.ResolvedNamespace;
        }
        if (newBindings != null)
            context.PrefixNamespaceBindings = newBindings;
        if (newConstructorBindings != null)
            context.EnclosingConstructorBindings = newConstructorBindings;

        // Evaluate content — also collect namespace nodes from computed namespace constructors
        var childIds = new List<NodeId>();
        var contentNsDecls = new List<NamespaceBinding>();
        StringBuilder? pendingText = null;

        void FlushPendingText()
        {
            if (pendingText != null && pendingText.Length > 0)
            {
                var textValue = pendingText.ToString();
                // Boundary whitespace stripping is handled at compile time in the
                // optimizer's FilterBoundaryWhitespace. No runtime stripping needed.
                var textId = store.AllocateId();
                var textNode = new XdmText
                {
                    Id = textId,
                    Document = constructedDocId,
                    Value = textValue
                };
                textNode.Parent = elemId;
                store.RegisterNode(textNode);
                childIds.Add(textId);
                pendingText.Clear();
            }
        }

        foreach (var contentOp in ContentOperators)
        {
            // Direct child constructors produce freshly constructed elements — copy-namespaces
            // mode (preserve/no-preserve) only applies when copying a pre-existing node
            // (e.g. from a variable reference in an enclosed expression), not when constructing
            // a new element inline.  See XQuery 3.1 §3.7.1: "The copy-namespaces declaration
            // controls the namespace bindings that are assigned when an existing element node
            // is copied by an element constructor."
            bool isDirectChildConstructor = contentOp is ElementConstructorOperator { IsDirectChild: true };
            bool isFirstAtomicInOp = true;
            await foreach (var contentResult in contentOp.ExecuteAsync(context))
            {
                if (contentResult is XdmElement childElem)
                {
                    FlushPendingText();
                    // Deep-copy the element into the constructed tree
                    var copyId = DeepCopyNode(childElem, store, constructedDocId, elemId);
                    if (!isDirectChildConstructor)
                        ApplyCopyNamespacesMode(copyId, store, context.CopyNamespacesMode, context.EnclosingConstructorBindings);
                    childIds.Add(copyId);
                    // Reset so the next atomic value after a node doesn't get a leading space.
                    // Per XQuery §3.7.1.3, spaces only separate *adjacent* atomic values.
                    isFirstAtomicInOp = true;
                }
                else if (contentResult is XdmDocument doc)
                {
                    // Document node: unwrap children. Per XQuery spec §3.7.1, document nodes in
                    // element content are replaced by their children. Text children merge with
                    // pending text to ensure adjacent text nodes are properly concatenated.
                    foreach (var docChildId in doc.Children)
                    {
                        var docChild = store.GetNode(docChildId);
                        if (docChild is XdmText docText)
                        {
                            // Merge text from nested doc into pending text
                            pendingText ??= new StringBuilder();
                            pendingText.Append(docText.Value);
                        }
                        else if (docChild != null)
                        {
                            FlushPendingText();
                            var copyId = DeepCopyNode(docChild, store, constructedDocId, elemId);
                            ApplyCopyNamespacesMode(copyId, store, context.CopyNamespacesMode, context.EnclosingConstructorBindings);
                            childIds.Add(copyId);
                        }
                    }
                    isFirstAtomicInOp = true;
                }
                else if (contentResult is XdmText text)
                {
                    // Merge adjacent text
                    pendingText ??= new StringBuilder();
                    pendingText.Append(text.Value);
                    isFirstAtomicInOp = true;
                }
                else if (contentResult is XdmNamespace nsNode)
                {
                    // Namespace constructor: add to element's namespace declarations.
                    // XQDY0102: if the same prefix is declared more than once with different
                    // URIs within a single element constructor, raise an error.
                    var nsAttrId = store is XdmDocumentStore ds
                        ? ds.ResolveNamespace(nsNode.Uri)
                        : NamespaceId.None;
                    if (store is XdmDocumentStore ds2)
                        ds2.RegisterNamespace(nsNode.Uri, nsAttrId);
                    foreach (var existing in contentNsDecls)
                    {
                        if (existing.Prefix == (nsNode.Prefix ?? "") && existing.Namespace != nsAttrId)
                            throw new XQueryRuntimeException("XQDY0102",
                                $"Namespace constructor: prefix '{nsNode.Prefix}' bound to multiple URIs within an element constructor");
                    }
                    contentNsDecls.Add(new NamespaceBinding(nsNode.Prefix ?? "", nsAttrId));
                }
                else if (contentResult is XdmComment || contentResult is XdmProcessingInstruction)
                {
                    FlushPendingText();
                    var copyId = DeepCopyNode((XdmNode)contentResult, store, constructedDocId, elemId);
                    childIds.Add(copyId);
                    isFirstAtomicInOp = true;
                }
                else if (contentResult is XdmAttribute)
                {
                    // XQTY0024: attribute must not appear after a non-attribute child in content
                    if (childIds.Count > 0 || (pendingText != null && pendingText.Length > 0))
                        throw new XQueryRuntimeException("XQTY0024",
                            "Attribute node in element content must precede all other nodes");
                    var contentAttr = (XdmAttribute)contentResult;
                    CheckDuplicateAttr(contentAttr);
                    var newAttrId = store.AllocateId();
                    var newAttr = new XdmAttribute
                    {
                        Id = newAttrId,
                        Document = constructedDocId,
                        Namespace = contentAttr.Namespace,
                        LocalName = contentAttr.LocalName,
                        Prefix = contentAttr.Prefix,
                        Value = contentAttr.Value,
                        TypeAnnotation = contentAttr.TypeAnnotation,
                        IsId = contentAttr.IsId
                    };
                    newAttr.Parent = elemId;
                    store.RegisterNode(newAttr);
                    attrIds.Add(newAttrId);
                }
                else if (contentResult is List<object?> arrayItems)
                {
                    // XQuery 3.1 §3.7.3.1: arrays in element content are replaced
                    // by their members (recursively flattened into the content sequence).
                    bool isFirstAtomicInArray = true;
                    foreach (var member in FlattenArrayMembers(arrayItems))
                    {
                        if (member is XdmAttribute arrAttr)
                        {
                            // Attributes from array members are added to the element
                            var newAttrId = store.AllocateId();
                            var newAttr = new XdmAttribute
                            {
                                Id = newAttrId,
                                Document = constructedDocId,
                                Namespace = arrAttr.Namespace,
                                LocalName = arrAttr.LocalName,
                                Prefix = arrAttr.Prefix,
                                Value = arrAttr.Value,
                                TypeAnnotation = arrAttr.TypeAnnotation,
                                IsId = arrAttr.IsId
                            };
                            newAttr.Parent = elemId;
                            store.RegisterNode(newAttr);
                            attrIds.Add(newAttrId);
                            isFirstAtomicInArray = true;
                        }
                        else if (member is XdmElement arrElem)
                        {
                            FlushPendingText();
                            var copyId = DeepCopyNode(arrElem, store, constructedDocId, elemId);
                            ApplyCopyNamespacesMode(copyId, store, context.CopyNamespacesMode, context.EnclosingConstructorBindings);
                            childIds.Add(copyId);
                            isFirstAtomicInArray = true; // reset after node
                        }
                        else if (member is XdmText arrText)
                        {
                            pendingText ??= new StringBuilder();
                            pendingText.Append(arrText.Value);
                            isFirstAtomicInArray = true; // reset after node
                        }
                        else if (member is XdmDocument arrDoc)
                        {
                            foreach (var docChildId in arrDoc.Children)
                            {
                                var docChild = store.GetNode(docChildId);
                                if (docChild is XdmText docText)
                                {
                                    pendingText ??= new StringBuilder();
                                    pendingText.Append(docText.Value);
                                }
                                else if (docChild != null)
                                {
                                    FlushPendingText();
                                    var copyId = DeepCopyNode(docChild, store, constructedDocId, elemId);
                                    ApplyCopyNamespacesMode(copyId, store, context.CopyNamespacesMode, context.EnclosingConstructorBindings);
                                    childIds.Add(copyId);
                                }
                            }
                            isFirstAtomicInArray = true; // reset after node
                        }
                        else if (member is XdmComment || member is XdmProcessingInstruction)
                        {
                            FlushPendingText();
                            var copyId = DeepCopyNode((XdmNode)member, store, constructedDocId, elemId);
                            childIds.Add(copyId);
                            isFirstAtomicInArray = true; // reset after node
                        }
                        else if (member != null)
                        {
                            pendingText ??= new StringBuilder();
                            var atomized = context.AtomizeWithNodes(member);
                            var atomicText = Functions.ConcatFunction.XQueryStringValue(atomized);
                            if (!isFirstAtomicInArray)
                                pendingText.Append(' ');
                            pendingText.Append(atomicText);
                            isFirstAtomicInArray = false;
                        }
                    }
                    isFirstAtomicInOp = false;
                }
                else if (contentResult != null)
                {
                    // Atomic values become text nodes; merge adjacent text
                    // Per XQuery 3.1 §3.7.1.3: adjacent atomic values from the SAME
                    // expression are separated by spaces. Values from different
                    // content expressions concatenate without a separator.
                    pendingText ??= new StringBuilder();
                    var atomized = context.AtomizeWithNodes(contentResult);
                    var atomicText = Functions.ConcatFunction.XQueryStringValue(atomized);
                    if (!isFirstAtomicInOp)
                        pendingText.Append(' ');
                    pendingText.Append(atomicText);
                    isFirstAtomicInOp = false;
                }
            }
        }

        FlushPendingText();

        // Restore old namespace bindings
        if (newBindings != null)
            context.PrefixNamespaceBindings = oldBindings;
        if (newConstructorBindings != null)
            context.EnclosingConstructorBindings = oldConstructorBindings;

        // Merge content namespace declarations (from computed namespace constructors)
        // These are added to the element's namespace declarations below.

        // Build namespace declarations from element name + xmlns: attributes
        var nsDecls = new List<NamespaceBinding>();
        var nsPrefixesSeen = new HashSet<string>();
        if (nsId != NamespaceId.None)
        {
            var prefix = elemName.Prefix ?? "";
            nsDecls.Add(new NamespaceBinding(prefix, nsId));
            nsPrefixesSeen.Add(prefix);
        }

        // Extract namespace declarations from xmlns: attributes
        // These were processed as regular attributes but need to be in NamespaceDeclarations
        var nonNsAttrIds = new List<NodeId>();
        foreach (var attrId in attrIds)
        {
            if (store.GetNode(attrId) is XdmAttribute attr)
            {
                // xmlns:prefix="uri" → namespace declaration (never a regular attribute)
                if (attr.Prefix == "xmlns")
                {
                    if (!nsPrefixesSeen.Contains(attr.LocalName))
                    {
                        var nsAttrId = store is XdmDocumentStore ds
                            ? ds.ResolveNamespace(attr.Value)
                            : NamespaceId.None;
                        nsDecls.Add(new NamespaceBinding(attr.LocalName, nsAttrId));
                        if (store is XdmDocumentStore ds2)
                            ds2.RegisterNamespace(attr.Value, nsAttrId);
                        nsPrefixesSeen.Add(attr.LocalName);
                    }
                    continue;
                }
                // xmlns="uri" → default namespace declaration (never a regular attribute)
                if (string.IsNullOrEmpty(attr.Prefix) && attr.LocalName == "xmlns")
                {
                    if (!nsPrefixesSeen.Contains(""))
                    {
                        var nsAttrId = string.IsNullOrEmpty(attr.Value)
                            ? NamespaceId.None
                            : (store is XdmDocumentStore ds3 ? ds3.ResolveNamespace(attr.Value) : NamespaceId.None);
                        nsDecls.Add(new NamespaceBinding("", nsAttrId));
                        if (!string.IsNullOrEmpty(attr.Value) && store is XdmDocumentStore ds4)
                            ds4.RegisterNamespace(attr.Value, nsAttrId);
                        nsPrefixesSeen.Add("");
                    }
                    continue;
                }
            }
            nonNsAttrIds.Add(attrId);
        }

        // Merge in-content namespace constructors. Per XQuery 3.1 §3.9.3, namespace
        // nodes added to an element take precedence — if a content namespace constructor
        // uses a prefix that conflicts with the element's own prefix or an xmlns: attr,
        // the element/attribute is renamed to a fresh prefix.
        int genCounterNs = 1;
        foreach (var contentNs in contentNsDecls)
        {
            if (string.IsNullOrEmpty(contentNs.Prefix) && contentNs.Namespace != NamespaceId.None && nsId == NamespaceId.None)
            {
                throw new XQueryRuntimeException("XQDY0102",
                    "Namespace constructor: cannot bind default namespace to non-empty URI on element in no namespace");
            }
            if (!nsPrefixesSeen.Contains(contentNs.Prefix))
            {
                nsDecls.Add(contentNs);
                nsPrefixesSeen.Add(contentNs.Prefix);
            }
            else
            {
                // Prefix already bound — check if it's the same URI
                var existingIdx = nsDecls.FindIndex(d => d.Prefix == contentNs.Prefix);
                if (existingIdx >= 0 && nsDecls[existingIdx].Namespace != contentNs.Namespace)
                {
                    // Conflict: content ns constructor takes precedence.
                    // If the conflicting binding came from the element's own prefix,
                    // rename the element to use a fresh prefix.
                    var oldBinding = nsDecls[existingIdx];
                    var elemPrefix = elemName.Prefix ?? "";
                    if (oldBinding.Prefix == elemPrefix && oldBinding.Namespace == nsId)
                    {
                        // Rename the element's prefix
                        string newElemPrefix;
                        var allPrefixes = new HashSet<string>(nsPrefixesSeen);
                        do { newElemPrefix = $"ns{genCounterNs++}"; } while (allPrefixes.Contains(newElemPrefix));

                        // Replace the old binding with the element's new prefix
                        nsDecls[existingIdx] = new NamespaceBinding(newElemPrefix, nsId);
                        nsPrefixesSeen.Add(newElemPrefix);

                        // Update the element name to use the new prefix
                        elemName = new QName(elemName.Namespace, elemName.LocalName, newElemPrefix)
                        {
                            ExpandedNamespace = elemName.ExpandedNamespace,
                            RuntimeNamespace = elemName.RuntimeNamespace
                        };
                    }
                    // Now add the content namespace binding
                    nsDecls.Add(contentNs);
                }
            }
        }

        // Build a prefix→NamespaceId map from the current nsDecls so we can detect
        // prefix/URI conflicts for prefixed attributes being added from copied nodes.
        var prefixToNs = new Dictionary<string, NamespaceId>(StringComparer.Ordinal);
        foreach (var d in nsDecls) prefixToNs[d.Prefix] = d.Namespace;

        // For each prefixed attribute, ensure the element has a namespace binding that
        // maps its prefix to the attribute's namespace URI. If the prefix is already
        // bound to a different URI, generate a fresh prefix and rewrite the attribute.
        int genCounter = 1;
        for (int i = 0; i < nonNsAttrIds.Count; i++)
        {
            var attrId = nonNsAttrIds[i];
            if (store.GetNode(attrId) is not XdmAttribute attr2) continue;
            if (string.IsNullOrEmpty(attr2.Prefix) || attr2.Prefix == "xmlns") continue;
            // "xml" prefix is implicit and must not be declared explicitly.
            if (attr2.Prefix == "xml") continue;
            // Skip attrs that are not actually in a namespace (shouldn't happen for prefixed).
            if (attr2.Namespace == NamespaceId.None) continue;

            if (prefixToNs.TryGetValue(attr2.Prefix, out var existingNs))
            {
                if (existingNs == attr2.Namespace)
                    continue; // already bound correctly
                // Conflict: same prefix already bound to a different URI. Generate a
                // fresh prefix and rewrite the attribute to use it.
                string newPrefix;
                do { newPrefix = $"ns{genCounter++}"; } while (prefixToNs.ContainsKey(newPrefix));
                var renamed = new XdmAttribute
                {
                    Id = attr2.Id,
                    Document = attr2.Document,
                    Namespace = attr2.Namespace,
                    LocalName = attr2.LocalName,
                    Prefix = newPrefix,
                    Value = attr2.Value,
                    TypeAnnotation = attr2.TypeAnnotation,
                    IsId = attr2.IsId
                };
                renamed.Parent = attr2.Parent;
                store.RegisterNode(renamed);
                nsDecls.Add(new NamespaceBinding(newPrefix, attr2.Namespace));
                prefixToNs[newPrefix] = attr2.Namespace;
                nsPrefixesSeen.Add(newPrefix);
            }
            else
            {
                nsDecls.Add(new NamespaceBinding(attr2.Prefix, attr2.Namespace));
                prefixToNs[attr2.Prefix] = attr2.Namespace;
                nsPrefixesSeen.Add(attr2.Prefix);
            }
        }

        // (Content namespace declarations were already merged above, before attribute rename.)

        // Propagate namespace bindings from enclosing direct element constructors.
        // Per XQuery 3.1 §3.7.1: the in-scope namespaces of a constructed element include
        // those inherited from enclosing element constructors. Bindings that come from
        // the prolog (declare namespace) are NOT automatically inherited — only those added
        // by enclosing direct constructors are propagated.
        if (context.PrefixNamespaceBindings != null)
        {
            var prologBindings = context.PrologNamespaceBindings;
            foreach (var (prefix, uri) in context.PrefixNamespaceBindings)
            {
                // Skip internal markers
                if (prefix == "##default-element") continue;

                // Skip prefixes already declared on this element
                if (nsPrefixesSeen.Contains(prefix)) continue;

                // Only propagate bindings that were added by enclosing constructors,
                // not those from the prolog. A binding is "from an enclosing constructor"
                // if it's not in the prolog baseline, or if the URI differs (overridden).
                if (prologBindings != null)
                {
                    if (prologBindings.TryGetValue(prefix, out var prologUri) && prologUri == uri)
                        continue; // Same as prolog — skip (not from an enclosing constructor)
                }

                // Resolve the URI to a NamespaceId
                NamespaceId inheritedNsId;
                if (string.IsNullOrEmpty(uri))
                {
                    inheritedNsId = NamespaceId.None;
                }
                else
                {
                    inheritedNsId = store is XdmDocumentStore inheritDs
                        ? inheritDs.ResolveNamespace(uri)
                        : NamespaceId.None;
                    if (store is XdmDocumentStore inheritDs2)
                        inheritDs2.RegisterNamespace(uri, inheritedNsId);
                }

                nsDecls.Add(new NamespaceBinding(prefix, inheritedNsId));
                nsPrefixesSeen.Add(prefix);
            }
        }

        // If this element is in no namespace but the context has a default namespace
        // in scope (from prolog or enclosing constructor), add an explicit xmlns=""
        // undeclaration so that in-scope-prefixes and serialization work correctly.
        // Without this, the default namespace from the parent would leak through.
        if (nsId == NamespaceId.None && !nsPrefixesSeen.Contains(""))
        {
            bool hasDefaultNs = false;
            if (context.PrefixNamespaceBindings != null)
            {
                if (context.PrefixNamespaceBindings.TryGetValue("", out var defNs) && !string.IsNullOrEmpty(defNs))
                    hasDefaultNs = true;
                else if (context.PrefixNamespaceBindings.TryGetValue("##default-element", out var prologDefNs) && !string.IsNullOrEmpty(prologDefNs))
                    hasDefaultNs = true;
            }
            if (hasDefaultNs)
            {
                nsDecls.Add(new NamespaceBinding("", NamespaceId.None));
                nsPrefixesSeen.Add("");
            }
        }

        var elem = new XdmElement
        {
            Id = elemId,
            Document = constructedDocId,
            Namespace = nsId,
            LocalName = elemName.LocalName,
            Prefix = elemName.Prefix,
            Attributes = nonNsAttrIds,
            Children = childIds,
            NamespaceDeclarations = nsDecls,
            BaseUri = context.StaticBaseUri
        };
        elem.Parent = null;
        // Compute string value so atomization works on constructed elements
        elem._stringValue = ComputeStringValueFromChildren(childIds, store);
        store.RegisterNode(elem);

        yield return elem;
    }

    /// <summary>
    /// Computes the string value of an element from its children by walking text descendants.
    /// </summary>
    internal static string ComputeStringValueFromChildren(IReadOnlyList<NodeId> childIds, INodeProvider store)
    {
        if (childIds.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var childId in childIds)
        {
            var child = store.GetNode(childId);
            if (child is XdmText text)
                sb.Append(text.Value);
            else if (child is XdmElement childElem)
            {
                // Use pre-computed value if available, otherwise recurse
                var sv = childElem.StringValue;
                if (!string.IsNullOrEmpty(sv))
                    sb.Append(sv);
                else
                    sb.Append(ComputeStringValueFromChildren(childElem.Children, store));
            }
        }
        return sb.ToString();
    }

    private async Task<string> SerializeAsString(QueryExecutionContext context)
    {
        var sb = new StringBuilder();
        var name = !string.IsNullOrEmpty(Name.Prefix) ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        sb.Append('<').Append(name);

        // Serialize attributes
        foreach (var attrOp in AttributeOperators)
        {
            await foreach (var attrResult in attrOp.ExecuteAsync(context))
            {
                if (attrResult is XdmAttribute attr)
                {
                    var attrName = !string.IsNullOrEmpty(attr.Prefix) ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName;
                    sb.Append(' ').Append(attrName).Append("=\"").Append(EscapeXmlAttribute(attr.Value)).Append('"');
                }
            }
        }

        // Serialize content into a buffer to detect empty elements
        var contentBuf = new StringBuilder();
        foreach (var contentOp in ContentOperators)
        {
            await foreach (var contentResult in contentOp.ExecuteAsync(context))
            {
                if (contentResult is XdmNode node)
                    contentBuf.Append(node.StringValue);
                else if (contentResult != null)
                {
                    var atomized = context.AtomizeWithNodes(contentResult);
                    contentBuf.Append(ConcatFunction.XQueryStringValue(atomized));
                }
            }
        }

        if (contentBuf.Length == 0)
        {
            sb.Append("/>");
        }
        else
        {
            sb.Append('>').Append(contentBuf).Append("</").Append(name).Append('>');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Sentinel prefix used to mark a copied element as "no-inherit": in-scope-prefixes
    /// stops walking ancestors past an element carrying this marker.
    /// </summary>
    internal const string NoInheritMarkerPrefix = "\u0001no-inherit\u0001";

    /// <summary>
    /// Applies copy-namespaces semantics (XQuery 3.1 §3.9.3.1) to a freshly copied
    /// element that is being inserted into a constructed element. The root of the
    /// copy has its NamespaceDeclarations adjusted according to the declared mode.
    /// When the inherit flag is in effect, enclosingBindings is consulted so that the
    /// copy picks up the new parent's visible namespaces. The copy's own bindings
    /// (from the source) win on prefix conflict.
    /// </summary>
    internal static void ApplyCopyNamespacesMode(
        NodeId copyRootId,
        INodeBuilder store,
        Analysis.CopyNamespacesMode mode,
        IReadOnlyDictionary<string, string>? enclosingBindings = null)
    {
        if (store.GetNode(copyRootId) is not XdmElement root)
            return;

        bool preserve = mode == Analysis.CopyNamespacesMode.PreserveInherit
                     || mode == Analysis.CopyNamespacesMode.PreserveNoInherit;
        bool inherit = mode == Analysis.CopyNamespacesMode.PreserveInherit
                    || mode == Analysis.CopyNamespacesMode.NoPreserveInherit;

        if (!preserve)
        {
            // no-preserve: strip unused namespace declarations from ALL elements in the copy
            // (not just the root). Each element keeps only bindings used by itself or its
            // descendants (and xml).
            StripUnusedNamespacesRecursive(root, store);
            root = (XdmElement)store.GetNode(copyRootId)!;
        }

        var current = root.NamespaceDeclarations?.ToList() ?? new List<NamespaceBinding>();
        bool rootModified = false;

        if (inherit && enclosingBindings != null && enclosingBindings.Count > 0
            && store is XdmDocumentStore nsStore)
        {
            // Inherit: add the enclosing constructor's in-scope bindings to the copy root
            // AND all descendant elements. Each element's own bindings win on prefix conflict.
            var presentPrefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nb in current)
                presentPrefixes.Add(nb.Prefix);
            if (!string.IsNullOrEmpty(root.Prefix))
                presentPrefixes.Add(root.Prefix);

            var inheritedBindings = new List<NamespaceBinding>();
            foreach (var (prefix, uri) in enclosingBindings)
            {
                if (prefix == "##default-element") continue;
                if (prefix == "xml") continue;
                if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(uri)) continue;
                if (IsPredefinedBuiltinNamespace(prefix, uri)) continue;

                NamespaceId nsId = string.IsNullOrEmpty(uri)
                    ? NamespaceId.None
                    : nsStore.ResolveNamespace(uri);
                if (!string.IsNullOrEmpty(uri))
                    nsStore.RegisterNamespace(uri, nsId);

                // Add to root's declarations (via `current`)
                if (!presentPrefixes.Contains(prefix))
                {
                    // When inheriting the default namespace from an enclosing constructor,
                    // if the copied element is in no namespace, add an xmlns="" undeclaration
                    // instead. Without this, the parent's default namespace would leak through
                    // and the serializer would wrongly emit xmlns="..." on the copied element.
                    if (string.IsNullOrEmpty(prefix) && root.Namespace == NamespaceId.None)
                    {
                        current.Add(new NamespaceBinding("", NamespaceId.None));
                    }
                    else
                    {
                        current.Add(new NamespaceBinding(prefix, nsId));
                    }
                    presentPrefixes.Add(prefix);
                    rootModified = true;
                }
                inheritedBindings.Add(new NamespaceBinding(prefix, nsId));
            }

            // Propagate inherited bindings to all descendant elements
            if (inheritedBindings.Count > 0)
            {
                foreach (var childId in root.Children)
                {
                    if (store.GetNode(childId) is XdmElement childElem)
                        PropagateInheritedNamespaces(childElem, store, inheritedBindings);
                }
            }
        }

        if (!inherit)
        {
            // no-inherit: the copy must not see the surrounding constructor's
            // in-scope namespaces. Insert a sentinel that fn:in-scope-prefixes
            // (and any other walker) recognizes as a walk-stop marker.
            current.Add(new NamespaceBinding(NoInheritMarkerPrefix, NamespaceId.None));
            rootModified = true;
        }

        // If no-preserve was applied, StripUnusedNamespacesRecursive already rewrote the root;
        // we need to re-register with the updated declarations if we modified further.
        if (!rootModified && preserve)
            return;

        // Replace the copy root's namespace declarations.
        var newRoot = new XdmElement
        {
            Id = root.Id,
            Document = root.Document,
            Namespace = root.Namespace,
            LocalName = root.LocalName,
            Prefix = root.Prefix,
            Attributes = root.Attributes,
            Children = root.Children,
            NamespaceDeclarations = current,
            TypeAnnotation = root.TypeAnnotation
        };
        newRoot.Parent = root.Parent;
        newRoot._stringValue = root._stringValue;
        store.RegisterNode(newRoot);
    }

    /// <summary>
    /// Propagates inherited namespace bindings to an element and all its descendant elements.
    /// Each element's own bindings win on prefix conflict.
    /// </summary>
    private static void PropagateInheritedNamespaces(
        XdmElement elem, INodeBuilder store, List<NamespaceBinding> inheritedBindings)
    {
        var presentPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nb in elem.NamespaceDeclarations)
            presentPrefixes.Add(nb.Prefix);
        if (!string.IsNullOrEmpty(elem.Prefix))
            presentPrefixes.Add(elem.Prefix);

        var newDecls = elem.NamespaceDeclarations.ToList();
        bool modified = false;
        foreach (var nb in inheritedBindings)
        {
            if (!presentPrefixes.Contains(nb.Prefix))
            {
                newDecls.Add(nb);
                presentPrefixes.Add(nb.Prefix);
                modified = true;
            }
        }

        if (modified)
        {
            var newElem = new XdmElement
            {
                Id = elem.Id, Document = elem.Document, Namespace = elem.Namespace,
                LocalName = elem.LocalName, Prefix = elem.Prefix,
                Attributes = elem.Attributes, Children = elem.Children,
                NamespaceDeclarations = newDecls, TypeAnnotation = elem.TypeAnnotation
            };
            newElem.Parent = elem.Parent;
            newElem._stringValue = elem._stringValue;
            store.RegisterNode(newElem);
        }

        // Recurse into child elements
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmElement childElem)
                PropagateInheritedNamespaces(childElem, store, inheritedBindings);
        }
    }

    /// <summary>
    /// Returns true if the given prefix/URI binding is a predefined built-in namespace
    /// (e.g., fn, xs, xsi, math, map, array, local, err). These are statically known for
    /// name resolution but are not treated as in-scope namespace nodes on element copies.
    /// </summary>
    private static bool IsPredefinedBuiltinNamespace(string prefix, string uri)
    {
        return uri switch
        {
            "http://www.w3.org/2005/xpath-functions" => prefix == "fn",
            "http://www.w3.org/2001/XMLSchema" => prefix == "xs",
            "http://www.w3.org/2001/XMLSchema-instance" => prefix == "xsi",
            "http://www.w3.org/2005/xpath-functions/math" => prefix == "math",
            "http://www.w3.org/2005/xpath-functions/map" => prefix == "map",
            "http://www.w3.org/2005/xpath-functions/array" => prefix == "array",
            "http://www.w3.org/2005/xquery-local-functions" => prefix == "local",
            "http://www.w3.org/2005/xqt-errors" => prefix == "err",
            _ => false,
        };
    }

    /// <summary>
    /// Recursively strips unused namespace declarations from every element in a copy tree.
    /// Per no-preserve semantics, each element retains only bindings whose prefix is used
    /// by the element itself or its attributes (and xml).
    /// </summary>
    private static void StripUnusedNamespacesRecursive(XdmElement elem, INodeBuilder store)
    {
        // First recurse into children
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmElement childElem)
                StripUnusedNamespacesRecursive(childElem, store);
        }

        // Collect prefixes used by this element and its descendants
        var usedPrefixes = new HashSet<string>();
        CollectUsedPrefixes(elem, store, usedPrefixes);

        var current = elem.NamespaceDeclarations?.ToList();
        if (current == null || current.Count == 0)
            return;

        var filtered = current
            .Where(b => usedPrefixes.Contains(b.Prefix) || b.Prefix == "xml")
            .ToList();

        if (filtered.Count == current.Count)
            return; // Nothing stripped

        var newElem = new XdmElement
        {
            Id = elem.Id,
            Document = elem.Document,
            Namespace = elem.Namespace,
            LocalName = elem.LocalName,
            Prefix = elem.Prefix,
            Attributes = elem.Attributes,
            Children = elem.Children,
            NamespaceDeclarations = filtered,
            TypeAnnotation = elem.TypeAnnotation
        };
        newElem.Parent = elem.Parent;
        newElem._stringValue = elem._stringValue;
        store.RegisterNode(newElem);
    }

    private static void CollectUsedPrefixes(XdmElement elem, INodeProvider store, HashSet<string> used)
    {
        used.Add(elem.Prefix ?? "");
        foreach (var attrId in elem.Attributes)
        {
            if (store.GetNode(attrId) is XdmAttribute a)
            {
                // Only truly-prefixed attributes contribute (unprefixed attrs are in no namespace)
                if (!string.IsNullOrEmpty(a.Prefix))
                    used.Add(a.Prefix);
            }
        }
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmElement childElem)
                CollectUsedPrefixes(childElem, store, used);
        }
    }

    /// <summary>
    /// Recursively flattens XDM arrays (List&lt;object?&gt;) into individual items.
    /// Per XQuery 3.1 §3.7.3.1: arrays in element content are replaced by their members.
    /// </summary>
    private static IEnumerable<object?> FlattenArrayMembers(List<object?> array)
    {
        foreach (var member in array)
        {
            if (member is List<object?> nested)
            {
                foreach (var item in FlattenArrayMembers(nested))
                    yield return item;
            }
            else if (member is object?[] seqArr)
            {
                foreach (var item in seqArr)
                {
                    if (item is List<object?> innerArr)
                    {
                        foreach (var sub in FlattenArrayMembers(innerArr))
                            yield return sub;
                    }
                    else
                        yield return item;
                }
            }
            else
            {
                yield return member;
            }
        }
    }

    internal static NodeId DeepCopyNode(XdmNode source, INodeBuilder store, DocumentId docId, NodeId? parentId)
        => DeepCopyNode(source, store, docId, parentId, isRoot: true);

    internal static NodeId DeepCopyNode(XdmNode source, INodeBuilder store, DocumentId docId, NodeId? parentId, bool isRoot)
    {
        var newId = store.AllocateId();

        switch (source)
        {
            case XdmElement elem:
            {
                var newAttrs = new List<NodeId>();
                var newChildren = new List<NodeId>();

                // Materialize in-scope namespaces at the copy root so the constructed tree
                // carries bindings that originally came from ancestors of the source.
                // Per XQuery 3.1 copy-namespaces preserve/inherit default.
                IReadOnlyList<NamespaceBinding> nsDeclsCopy = elem.NamespaceDeclarations;
                if (isRoot)
                {
                    var merged = new Dictionary<string, NamespaceId>(StringComparer.Ordinal);
                    // Start with ancestors (walked from element to root), innermost wins — so
                    // we walk FROM the root DOWN. Use a stack.
                    var chain = new List<XdmElement>();
                    XdmNode? cursor = elem;
                    while (cursor != null)
                    {
                        if (cursor is XdmElement ce) chain.Add(ce);
                        cursor = cursor.Parent.HasValue ? store.GetNode(cursor.Parent.Value) as XdmNode : null;
                    }
                    // chain is [self, parent, grandparent, ...] — process from outermost.
                    for (int i = chain.Count - 1; i >= 0; i--)
                    {
                        var ce = chain[i];
                        if (ce.NamespaceDeclarations != null)
                        {
                            foreach (var nd in ce.NamespaceDeclarations)
                            {
                                if (nd.Prefix == NoInheritMarkerPrefix) continue;
                                merged[nd.Prefix] = nd.Namespace;
                            }
                        }
                        if (!string.IsNullOrEmpty(ce.Prefix) && ce.Namespace != NamespaceId.None)
                            merged.TryAdd(ce.Prefix, ce.Namespace);
                        else if (string.IsNullOrEmpty(ce.Prefix) && ce.Namespace != NamespaceId.None)
                            merged.TryAdd("", ce.Namespace);
                    }
                    // Drop "xml" — it's implicit and never serialized.
                    merged.Remove("xml");
                    // Build namespace declarations list from merged bindings.
                    // Keep xmlns="" (default namespace undeclaration) if the element itself
                    // explicitly declared it — the copy may be placed inside a parent with
                    // a default namespace and needs the undeclaration for correct serialization.
                    var list = new List<NamespaceBinding>(merged.Count);
                    foreach (var kv in merged)
                    {
                        // Drop empty-prefix/empty-URI only if it came purely from ancestor
                        // inheritance (the element itself has no such declaration). If the
                        // source element explicitly has xmlns="", keep it.
                        if (string.IsNullOrEmpty(kv.Key) && kv.Value == NamespaceId.None)
                        {
                            // Keep if the source element itself had this undeclaration
                            bool selfHasUndecl = elem.NamespaceDeclarations != null &&
                                elem.NamespaceDeclarations.Any(nd => string.IsNullOrEmpty(nd.Prefix) && nd.Namespace == NamespaceId.None);
                            if (!selfHasUndecl) continue;
                        }
                        list.Add(new NamespaceBinding(kv.Key, kv.Value));
                    }
                    nsDeclsCopy = list;
                }

                var newElem = new XdmElement
                {
                    Id = newId,
                    Document = docId,
                    Namespace = elem.Namespace,
                    LocalName = elem.LocalName,
                    Prefix = elem.Prefix,
                    Attributes = newAttrs,
                    Children = newChildren,
                    NamespaceDeclarations = nsDeclsCopy
                };
                newElem.Parent = parentId;
                store.RegisterNode(newElem);

                foreach (var attrId in elem.Attributes)
                {
                    if (store.GetNode(attrId) is XdmAttribute attr)
                    {
                        var newAttrId = store.AllocateId();
                        var newAttr = new XdmAttribute
                        {
                            Id = newAttrId,
                            Document = docId,
                            Namespace = attr.Namespace,
                            LocalName = attr.LocalName,
                            Prefix = attr.Prefix,
                            Value = attr.Value,
                            TypeAnnotation = attr.TypeAnnotation,
                            IsId = attr.IsId
                        };
                        newAttr.Parent = newId;
                        store.RegisterNode(newAttr);
                        newAttrs.Add(newAttrId);
                    }
                }

                foreach (var childId in elem.Children)
                {
                    var child = store.GetNode(childId);
                    if (child != null)
                    {
                        var childCopyId = DeepCopyNode(child, store, docId, newId, isRoot: false);
                        newChildren.Add(childCopyId);
                    }
                }

                // Compute string value for the copy
                newElem._stringValue = elem._stringValue ?? ComputeStringValueFromChildren(newChildren, store);

                return newId;
            }

            case XdmText text:
            {
                var newText = new XdmText
                {
                    Id = newId,
                    Document = docId,
                    Value = text.Value
                };
                newText.Parent = parentId;
                store.RegisterNode(newText);
                return newId;
            }

            case XdmComment comment:
            {
                var newComment = new XdmComment
                {
                    Id = newId,
                    Document = docId,
                    Value = comment.Value
                };
                newComment.Parent = parentId;
                store.RegisterNode(newComment);
                return newId;
            }

            case XdmProcessingInstruction pi:
            {
                var newPi = new XdmProcessingInstruction
                {
                    Id = newId,
                    Document = docId,
                    Target = pi.Target,
                    Value = pi.Value
                };
                newPi.Parent = parentId;
                store.RegisterNode(newPi);
                return newId;
            }

            default:
            {
                // Fallback for any other node type: create a text node from its string value
                var newText = new XdmText
                {
                    Id = newId,
                    Document = docId,
                    Value = source.StringValue ?? ""
                };
                newText.Parent = parentId;
                store.RegisterNode(newText);
                return newId;
            }
        }
    }

    private static string EscapeXmlAttribute(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}

/// <summary>
/// Attribute constructor operator — creates an XdmAttribute node.
/// Used for both direct attribute constructors (name="value") and computed
/// attribute constructors (attribute name { value }).
/// </summary>
public sealed class AttributeConstructorOperator : PhysicalOperator
{
    public required QName Name { get; init; }
    public required PhysicalOperator ValueOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeProvider as XdmDocumentStore;

        // Evaluate value
        var sb = new StringBuilder();
        await foreach (var item in ValueOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                var atomized = context.AtomizeWithNodes(item);
                sb.Append(Functions.ConcatFunction.XQueryStringValue(atomized));
            }
        }

        if (store != null)
        {
            var nsId = Name.Namespace;
            if (Name.ResolvedNamespace != null && nsId == NamespaceId.None)
                nsId = store.ResolveNamespace(Name.ResolvedNamespace);
            else if (Name.ResolvedNamespace != null)
                store.RegisterNamespace(Name.ResolvedNamespace, nsId);

            var attrId = store.AllocateNodeId();
            // xml:id attributes are always ID attributes per the xml:id specification.
            // Per the xml:id spec, the value is whitespace-normalized (leading/trailing stripped)
            // and must be a valid NCName to be recognized as an ID.
            var isXmlId = Name.LocalName == "id" &&
                (Name.ResolvedNamespace == "http://www.w3.org/XML/1998/namespace" ||
                 Name.Prefix == "xml");
            var attrValue = sb.ToString();
            if (isXmlId)
            {
                attrValue = attrValue.Trim();
                // Per xml:id spec, only valid NCName values are recognized as IDs
                if (!IsValidNCName(attrValue))
                    isXmlId = false;
            }
            var attr = new XdmAttribute
            {
                Id = attrId,
                Document = new DocumentId(0),
                Namespace = nsId,
                LocalName = Name.LocalName,
                Prefix = Name.Prefix,
                Value = attrValue,
                IsId = isXmlId
            };
            store.RegisterNode(attr);
            yield return attr;
        }
        else
        {
            // Fallback: return a synthetic attribute node
            var isXmlId = Name.LocalName == "id" && Name.Prefix == "xml";
            var fallbackValue = sb.ToString();
            if (isXmlId)
            {
                fallbackValue = fallbackValue.Trim();
                if (!IsValidNCName(fallbackValue))
                    isXmlId = false;
            }
            var attr = new XdmAttribute
            {
                Id = new NodeId(0),
                Document = new DocumentId(0),
                Namespace = NamespaceId.None,
                LocalName = Name.LocalName,
                Prefix = Name.Prefix,
                Value = fallbackValue,
                IsId = isXmlId
            };
            yield return attr;
        }
    }

    /// <summary>
    /// Checks if a string is a valid NCName per the XML Namespaces spec.
    /// NCName must start with a letter or underscore, followed by letters, digits,
    /// hyphens, underscores, or periods. No colons allowed.
    /// </summary>
    private static bool IsValidNCName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // First character must be a NameStartChar (excluding ':')
        var ch = value[0];
        if (!IsNameStartChar(ch))
            return false;

        // Subsequent characters must be NameChars (excluding ':')
        for (var i = 1; i < value.Length; i++)
        {
            if (!IsNameChar(value[i]))
                return false;
        }

        return true;
    }

    private static bool IsNameStartChar(char ch) =>
        ch == '_' ||
        (ch >= 'A' && ch <= 'Z') ||
        (ch >= 'a' && ch <= 'z') ||
        (ch >= '\u00C0' && ch <= '\u00D6') ||
        (ch >= '\u00D8' && ch <= '\u00F6') ||
        (ch >= '\u00F8' && ch <= '\u02FF') ||
        (ch >= '\u0370' && ch <= '\u037D') ||
        (ch >= '\u037F' && ch <= '\u1FFF') ||
        (ch >= '\u200C' && ch <= '\u200D') ||
        (ch >= '\u2070' && ch <= '\u218F') ||
        (ch >= '\u2C00' && ch <= '\u2FEF') ||
        (ch >= '\u3001' && ch <= '\uD7FF') ||
        (ch >= '\uF900' && ch <= '\uFDCF') ||
        (ch >= '\uFDF0' && ch <= '\uFFFD');

    private static bool IsNameChar(char ch) =>
        IsNameStartChar(ch) ||
        ch == '-' || ch == '.' ||
        (ch >= '0' && ch <= '9') ||
        ch == '\u00B7' ||
        (ch >= '\u0300' && ch <= '\u036F') ||
        (ch >= '\u203F' && ch <= '\u2040');
}

/// <summary>
/// Text constructor operator — creates an XdmText node.
/// Used for text { "content" } expressions.
/// </summary>
public sealed class TextConstructorOperator : PhysicalOperator
{
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeProvider as XdmDocumentStore;

        var sb = new StringBuilder();
        bool hasItems = false;
        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (hasItems)
                    sb.Append(' ');
                hasItems = true;
                var atomized = context.AtomizeWithNodes(item);
                sb.Append(Functions.ConcatFunction.XQueryStringValue(atomized));
            }
        }

        // Per XQuery 3.1 §3.7.3.4: if the content expression evaluates to the empty sequence,
        // no text node is constructed. But a zero-length string DOES produce a text node.
        if (!hasItems)
            yield break;

        if (store != null)
        {
            var textId = store.AllocateNodeId();
            var textNode = new XdmText
            {
                Id = textId,
                Document = new DocumentId(0),
                Value = sb.ToString()
            };
            store.RegisterNode(textNode);
            yield return textNode;
        }
        else
        {
            var textNode = new XdmText
            {
                Id = new NodeId(0),
                Document = new DocumentId(0),
                Value = sb.ToString()
            };
            yield return textNode;
        }
    }
}

/// <summary>
/// Comment constructor operator — creates an XdmComment node.
/// Used for comment { "content" } and &lt;!-- content --&gt; expressions.
/// </summary>
public sealed class CommentConstructorOperator : PhysicalOperator
{
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeProvider as XdmDocumentStore;

        var sb = new StringBuilder();
        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                var atomized = context.AtomizeWithNodes(item);
                sb.Append(Functions.ConcatFunction.XQueryStringValue(atomized));
            }
        }

        var value = sb.ToString();
        // XQDY0072: comment content must not contain '--' or end with '-'
        if (value.Contains("--"))
            throw new XQueryRuntimeException("XQDY0072",
                "Computed comment must not contain '--'");
        if (value.EndsWith('-'))
            throw new XQueryRuntimeException("XQDY0072",
                "Computed comment must not end with '-'");

        if (store != null)
        {
            var commentId = store.AllocateNodeId();
            var comment = new XdmComment
            {
                Id = commentId,
                Document = new DocumentId(0),
                Value = value
            };
            store.RegisterNode(comment);
            yield return comment;
        }
        else
        {
            var comment = new XdmComment
            {
                Id = new NodeId(0),
                Document = new DocumentId(0),
                Value = value
            };
            yield return comment;
        }
    }
}

/// <summary>
/// Processing instruction constructor operator — creates an XdmProcessingInstruction node.
/// Used for processing-instruction name { "content" } and &lt;?target content?&gt; expressions.
/// </summary>
/// <summary>
/// Computed namespace constructor: namespace prefix { "uri" }
/// Yields an XdmNamespace node that the parent ElementConstructor collects.
/// </summary>
public sealed class NamespaceNodeOperator : PhysicalOperator
{
    public string? DirectPrefix { get; init; }
    public PhysicalOperator? PrefixOperator { get; init; }
    public required PhysicalOperator UriOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        string prefix;
        if (DirectPrefix != null)
            prefix = DirectPrefix;
        else if (PrefixOperator != null)
        {
            prefix = "";
            await foreach (var p in PrefixOperator.ExecuteAsync(context))
            {
                var atomized = QueryExecutionContext.Atomize(p);
                // XPTY0004: the prefix expression must yield xs:string, xs:untypedAtomic,
                // or xs:NCName. Other atomic types (e.g. xs:anyURI, xs:duration) are errors.
                if (atomized is not null
                    and not string
                    and not PhoenixmlDb.Xdm.XsUntypedAtomic)
                {
                    throw new XQueryRuntimeException("XPTY0004",
                        $"Namespace constructor: prefix expression must be xs:string or xs:untypedAtomic, got {atomized.GetType().Name}");
                }
                prefix = atomized?.ToString()?.Trim() ?? "";
                break;
            }
        }
        else
            prefix = "";

        // XQDY0074: if prefix is non-empty it must be a valid NCName
        if (!string.IsNullOrEmpty(prefix))
        {
            try { System.Xml.XmlConvert.VerifyNCName(prefix); }
            catch
            {
                throw new XQueryRuntimeException("XQDY0074",
                    $"Namespace constructor: prefix '{prefix}' is not a valid NCName");
            }
        }

        var sb = new StringBuilder();
        await foreach (var item in UriOperator.ExecuteAsync(context))
        {
            var atomized = QueryExecutionContext.Atomize(item);
            if (atomized != null)
                sb.Append(atomized.ToString());
        }
        var uri = sb.ToString();

        // XQDY0101: cannot bind 'xml'/'xmlns' prefixes, and cannot bind any prefix
        // to the reserved XML/XMLNS namespaces. An empty prefix bound to the empty URI
        // is allowed (default namespace undeclaration). A non-empty prefix with an
        // empty URI is also forbidden.
        if (prefix == "xmlns")
            throw new XQueryRuntimeException("XQDY0101",
                "Namespace constructor: the 'xmlns' prefix cannot be declared");
        if (prefix == "xml" && uri != "http://www.w3.org/XML/1998/namespace")
            throw new XQueryRuntimeException("XQDY0101",
                "Namespace constructor: the 'xml' prefix can only be bound to the XML namespace");
        if (prefix != "xml" && uri == "http://www.w3.org/XML/1998/namespace")
            throw new XQueryRuntimeException("XQDY0101",
                "Namespace constructor: the XML namespace can only be bound to the 'xml' prefix");
        if (uri == "http://www.w3.org/2000/xmlns/")
            throw new XQueryRuntimeException("XQDY0101",
                "Namespace constructor: the XMLNS namespace cannot be bound to any prefix");
        if (string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(prefix))
            throw new XQueryRuntimeException("XQDY0101",
                $"Namespace constructor: prefix '{prefix}' cannot be bound to the empty URI");

        var nsNode = new XdmNamespace
        {
            Id = new NodeId(0),
            Document = new DocumentId(0),
            Prefix = prefix,
            Uri = uri
        };
        yield return nsNode;
    }
}

public sealed class PIConstructorOperator : PhysicalOperator
{
    public string? DirectTarget { get; init; }
    public PhysicalOperator? TargetOperator { get; init; }
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeProvider as XdmDocumentStore;

        // Determine target
        var target = DirectTarget;
        if (target == null && TargetOperator != null)
        {
            int count = 0;
            await foreach (var item in TargetOperator.ExecuteAsync(context))
            {
                if (count == 0)
                {
                    var atomized = context.AtomizeWithNodes(item);
                    // Per XQuery §3.7.3.5: the computed name must be xs:string, xs:untypedAtomic,
                    // or xs:NCName. Other types (xs:anyURI, xs:duration, etc.) raise XPTY0004.
                    if (atomized != null && atomized is not string
                        && atomized is not Xdm.XsUntypedAtomic)
                    {
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Processing instruction name must be xs:string or xs:untypedAtomic, got {atomized.GetType().Name}");
                    }
                    target = atomized?.ToString()?.Trim() ?? "";
                }
                count++;
                if (count > 1)
                    throw new XQueryRuntimeException("XPTY0004",
                        "Processing instruction name must be a single atomic value");
            }
            if (count == 0)
                throw new XQueryRuntimeException("XPTY0004",
                    "Processing instruction name cannot be an empty sequence");
        }
        target ??= "";
        // XQDY0064: PI target cannot be 'xml' (case-insensitive) per XQuery 3.1 §3.7.3.5
        if (target.Equals("xml", StringComparison.OrdinalIgnoreCase))
            throw new XQueryRuntimeException("XQDY0064", "Processing instruction target cannot be 'xml'");
        if (target.Contains(':'))
            throw new XQueryRuntimeException("XQDY0041",
                $"Processing instruction target '{target}' cannot contain ':'");
        try { System.Xml.XmlConvert.VerifyNCName(target); }
        catch
        {
            throw new XQueryRuntimeException("XQDY0041",
                $"Processing instruction target '{target}' is not a valid NCName");
        }

        // Evaluate content
        var sb = new StringBuilder();
        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                var atomized = context.AtomizeWithNodes(item);
                sb.Append(Functions.ConcatFunction.XQueryStringValue(atomized));
            }
        }

        // Per XQuery §3.7.3.5: leading whitespace in computed PI content is stripped
        var value = sb.ToString().TrimStart();
        // XQDY0026: PI content must not contain '?>'
        if (value.Contains("?>"))
            throw new XQueryRuntimeException("XQDY0026",
                "Processing instruction content must not contain '?>'");

        if (store != null)
        {
            var piId = store.AllocateNodeId();
            var pi = new XdmProcessingInstruction
            {
                Id = piId,
                Document = new DocumentId(0),
                Target = target,
                Value = value
            };
            store.RegisterNode(pi);
            yield return pi;
        }
        else
        {
            var pi = new XdmProcessingInstruction
            {
                Id = new NodeId(0),
                Document = new DocumentId(0),
                Target = target,
                Value = value
            };
            yield return pi;
        }
    }
}

/// <summary>
/// Document constructor operator — creates an XdmDocument node wrapping content.
/// Used for document { content } expressions.
/// </summary>
public sealed class DocumentConstructorOperator : PhysicalOperator
{
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeProvider as XdmDocumentStore;

        if (store == null)
        {
            // Without a store, delegate to content directly
            await foreach (var item in ContentOperator.ExecuteAsync(context))
            {
                yield return item;
            }
            yield break;
        }

        var constructedDocId = new DocumentId(0);
        var docId = store.AllocateNodeId();
        var childIds = new List<NodeId>();
        StringBuilder? pendingText = null;

        void FlushPendingText()
        {
            if (pendingText != null && pendingText.Length > 0)
            {
                var textId = store.AllocateNodeId();
                var textNode = new XdmText
                {
                    Id = textId,
                    Document = constructedDocId,
                    Value = pendingText.ToString()
                };
                textNode.Parent = docId;
                store.RegisterNode(textNode);
                childIds.Add(textId);
                pendingText.Clear();
            }
        }

        NodeId? docElement = null;
        bool lastWasAtomic = false;

        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item is XdmElement childElem)
            {
                FlushPendingText();
                var copyId = ElementConstructorOperator.DeepCopyNode(childElem, store, constructedDocId, docId);
                childIds.Add(copyId);
                docElement ??= copyId;
                lastWasAtomic = false;
            }
            else if (item is XdmComment || item is XdmProcessingInstruction)
            {
                FlushPendingText();
                var copyId = ElementConstructorOperator.DeepCopyNode((XdmNode)item, store, constructedDocId, docId);
                childIds.Add(copyId);
                lastWasAtomic = false;
            }
            else if (item is XdmAttribute)
            {
                // XPTY0004: document content sequence may not contain attribute nodes
                throw new XQueryRuntimeException("XPTY0004",
                    "A document constructor cannot contain attribute nodes");
            }
            else if (item is XdmText text)
            {
                pendingText ??= new StringBuilder();
                pendingText.Append(text.Value);
                lastWasAtomic = false;
            }
            else if (item is XdmDocument nestedDoc)
            {
                // Unwrap nested document: per XQuery spec, document nodes in content
                // are replaced by their children. Text children merge with pending text.
                foreach (var nestedChildId in nestedDoc.Children)
                {
                    var nestedChild = store.GetNode(nestedChildId);
                    if (nestedChild is XdmText nestedText)
                    {
                        // Merge text from nested doc into pending text
                        pendingText ??= new StringBuilder();
                        pendingText.Append(nestedText.Value);
                    }
                    else if (nestedChild != null)
                    {
                        FlushPendingText();
                        var copyId = ElementConstructorOperator.DeepCopyNode(nestedChild, store, constructedDocId, docId);
                        childIds.Add(copyId);
                        if (nestedChild is XdmElement && docElement == null)
                            docElement = copyId;
                    }
                }
                lastWasAtomic = false;
            }
            else if (item != null)
            {
                pendingText ??= new StringBuilder();
                var atomicVal = context.AtomizeWithNodes(item)?.ToString() ?? "";
                // Space-separate consecutive atomic values per XQuery 3.1 §3.7.3.4
                // Empty strings still count as atomic values requiring a separator
                if (lastWasAtomic)
                    pendingText.Append(' ');
                pendingText.Append(atomicVal);
                lastWasAtomic = true;
            }
        }

        FlushPendingText();

        var doc = new XdmDocument
        {
            Id = docId,
            Document = constructedDocId,
            Children = childIds,
            DocumentElement = docElement,
            BaseUri = context.StaticBaseUri
        };
        doc.Parent = null;

        // Pre-compute string value by concatenating all descendant text nodes
        var svBuilder = new StringBuilder();
        ComputeDocumentStringValue(doc, store, svBuilder);
        doc._stringValue = svBuilder.ToString();

        store.RegisterNode(doc);

        yield return doc;
    }

    /// <summary>
    /// Recursively computes the string value of a document node by concatenating all descendant text nodes.
    /// </summary>
    private static void ComputeDocumentStringValue(XdmDocument doc, XdmDocumentStore store, StringBuilder sb)
    {
        foreach (var childId in doc.Children)
        {
            var child = store.GetNode(childId);
            if (child is XdmText text)
                sb.Append(text.Value);
            else if (child is XdmElement elem)
                CollectTextDescendants(elem, store, sb);
        }
    }

    private static void CollectTextDescendants(XdmElement elem, XdmDocumentStore store, StringBuilder sb)
    {
        foreach (var childId in elem.Children)
        {
            var child = store.GetNode(childId);
            if (child is XdmText text)
                sb.Append(text.Value);
            else if (child is XdmElement childElem)
                CollectTextDescendants(childElem, store, sb);
        }
    }
}

/// <summary>
/// Computed element constructor operator — element { nameExpr } { contentExpr }.
/// The element name is computed at runtime from an expression.
/// </summary>
public sealed class ComputedElementConstructorOperator : PhysicalOperator
{
    /// <summary>
    /// Default XQuery statically-known namespace prefixes (XQuery 3.1 §2.1.1).
    /// Used as fallback when PrefixNamespaceBindings is null (no prolog).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> DefaultPrefixBindings =
        new Dictionary<string, string>
        {
            ["xml"] = "http://www.w3.org/XML/1998/namespace",
            ["xs"] = "http://www.w3.org/2001/XMLSchema",
            ["xsi"] = "http://www.w3.org/2001/XMLSchema-instance",
            ["fn"] = "http://www.w3.org/2005/xpath-functions",
            ["math"] = "http://www.w3.org/2005/xpath-functions/math",
            ["array"] = "http://www.w3.org/2005/xpath-functions/array",
            ["map"] = "http://www.w3.org/2005/xpath-functions/map",
            ["local"] = "http://www.w3.org/2005/xquery-local-functions"
        };

    public required PhysicalOperator NameOperator { get; init; }
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate name — preserve QName namespace from EQName expressions
        QName name;
        int nameCount = 0;
        object? firstName = null;
        await foreach (var nameResult in NameOperator.ExecuteAsync(context))
        {
            nameCount++;
            if (nameCount > 1)
                throw new XQueryRuntimeException("XPTY0004",
                    "Element name must be a single atomic value, got a sequence");
            firstName = nameResult;
        }
        if (nameCount == 0)
            throw new XQueryRuntimeException("XPTY0004",
                "Element name cannot be an empty sequence");

        if (firstName is QName qn)
        {
            // Per XQuery 3.1 §3.9.3.1: if the QName has no namespace and no prefix,
            // apply the default element namespace (from prolog or enclosing direct constructor).
            if (qn.ResolvedNamespace == null && string.IsNullOrEmpty(qn.Prefix))
            {
                string? defaultNs = null;
                if (context.PrefixNamespaceBindings != null)
                {
                    if (context.PrefixNamespaceBindings.TryGetValue("", out var enclosingDefaultNs)
                        && !string.IsNullOrEmpty(enclosingDefaultNs))
                        defaultNs = enclosingDefaultNs;
                    else if (context.PrefixNamespaceBindings.TryGetValue("##default-element", out var prologDefaultNs)
                        && !string.IsNullOrEmpty(prologDefaultNs))
                        defaultNs = prologDefaultNs;
                }
                if (defaultNs != null)
                    qn = new QName(qn.Namespace, qn.LocalName, qn.Prefix) { ExpandedNamespace = defaultNs };
            }
            name = qn;
        }
        else
        {
            var nameVal = (context.AtomizeWithNodes(firstName)?.ToString() ?? "").Trim();
            string localName;
            string? prefix = null;
            string? expandedNs = null;

            if (nameVal.StartsWith("Q{", StringComparison.Ordinal))
            {
                // Find the closing '}' that separates the namespace URI from the local name.
                // The namespace URI may itself contain '}' (from resolved character references),
                // so we search from the end: the last '}' followed by a valid NCName is the delimiter.
                int closeBrace = -1;
                for (int i = nameVal.Length - 1; i >= 2; i--)
                {
                    if (nameVal[i] == '}' && i + 1 < nameVal.Length && IsValidNCNameStart(nameVal[i + 1]))
                    {
                        closeBrace = i;
                        break;
                    }
                }
                // Fallback: if no '}' followed by NCName start found, try first '}' after Q{
                if (closeBrace < 0)
                    closeBrace = nameVal.IndexOf('}', 2);
                if (closeBrace > 1)
                {
                    var rawUri = nameVal[2..closeBrace];
                    // XQDY0074: runtime Q{uri}local strings cannot contain '{' or '}' in the URI.
                    // In source-level EQNames, these appear only via character references; in computed
                    // string values the grammar forbids them.
                    if (rawUri.Contains('{') || rawUri.Contains('}'))
                        throw new XQueryRuntimeException("XQDY0074",
                            $"'{nameVal}' is not a valid expanded QName: namespace URI contains '{{' or '}}'");
                    // Per XQuery 3.1 §3.9.3.1: whitespace in BracedURILiteral is
                    // normalized — leading/trailing trimmed, internal runs collapsed.
                    expandedNs = CollapseWhitespace(rawUri);
                    localName = nameVal[(closeBrace + 1)..];
                }
                else
                    localName = nameVal;
            }
            else if (nameVal.Contains(':'))
            {
                var parts = nameVal.Split(':', 2);
                prefix = parts[0];
                localName = parts[1];
                if (prefix == "xml")
                    expandedNs = "http://www.w3.org/XML/1998/namespace";
                else if (context.PrefixNamespaceBindings?.TryGetValue(prefix, out var nsUri) == true)
                    expandedNs = nsUri;
                else if (DefaultPrefixBindings.TryGetValue(prefix, out var defaultNsUri))
                    expandedNs = defaultNsUri;
                else
                    throw new XQueryRuntimeException("XQDY0074",
                        $"Namespace prefix '{prefix}' has not been declared");
            }
            else
            {
                localName = nameVal;
                // Per XQuery 3.1 §3.9.3.1: unprefixed computed element names use the
                // default element namespace. Check enclosing direct element constructor's
                // xmlns first, then fall back to prolog's declare default element namespace.
                if (context.PrefixNamespaceBindings != null)
                {
                    if (context.PrefixNamespaceBindings.TryGetValue("", out var enclosingDefaultNs)
                        && !string.IsNullOrEmpty(enclosingDefaultNs))
                    {
                        expandedNs = enclosingDefaultNs;
                    }
                    else if (context.PrefixNamespaceBindings.TryGetValue("##default-element", out var prologDefaultNs)
                        && !string.IsNullOrEmpty(prologDefaultNs))
                    {
                        expandedNs = prologDefaultNs;
                    }
                }
            }
            name = new QName(NamespaceId.None, localName, prefix) { ExpandedNamespace = expandedNs };
        }

        // XQDY0074: localName and prefix must be valid NCNames
        if (!IsValidNCName(name.LocalName))
            throw new XQueryRuntimeException("XQDY0074",
                $"'{name.LocalName}' is not a valid NCName for an element");
        if (name.Prefix != null && !IsValidNCName(name.Prefix))
            throw new XQueryRuntimeException("XQDY0074",
                $"'{name.Prefix}' is not a valid NCName for a prefix");

        // XQDY0096: element name namespace checks (XQuery 3.1 §3.9.3.1)
        var resolvedNs = name.ResolvedNamespace;
        // Cannot be in the xmlns namespace
        if (resolvedNs == "http://www.w3.org/2000/xmlns/")
            throw new XQueryRuntimeException("XQDY0096",
                "Computed element name cannot be in the 'http://www.w3.org/2000/xmlns/' namespace");
        // Prefix 'xml' must map to the XML namespace, and vice versa
        if (name.Prefix == "xml" && resolvedNs != null && resolvedNs != "http://www.w3.org/XML/1998/namespace")
            throw new XQueryRuntimeException("XQDY0096",
                "Prefix 'xml' must be bound to 'http://www.w3.org/XML/1998/namespace'");
        if (resolvedNs == "http://www.w3.org/XML/1998/namespace")
        {
            // Auto-assign xml prefix when no prefix given, error if wrong prefix
            if (string.IsNullOrEmpty(name.Prefix))
                name = new QName(name.Namespace, name.LocalName, "xml") { ExpandedNamespace = name.ExpandedNamespace, RuntimeNamespace = name.RuntimeNamespace };
            else if (name.Prefix != "xml")
                throw new XQueryRuntimeException("XQDY0096",
                    "Only prefix 'xml' can be bound to 'http://www.w3.org/XML/1998/namespace'");
        }
        // Prefix 'xmlns' is always reserved
        if (name.Prefix == "xmlns")
            throw new XQueryRuntimeException("XQDY0096",
                "Prefix 'xmlns' cannot be used in a computed element constructor");

        var delegateOp = new ElementConstructorOperator
        {
            Name = name,
            AttributeOperators = Array.Empty<PhysicalOperator>(),
            ContentOperators = new[] { ContentOperator }
        };

        await foreach (var result in delegateOp.ExecuteAsync(context))
        {
            yield return result;
        }
    }

    internal static bool IsValidNCNameStart(char c) => c == '_' || char.IsLetter(c);

    private static bool IsValidNCName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!IsValidNCNameStart(name[0])) return false;
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_'
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.SpacingCombiningMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.EnclosingMark)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Collapses whitespace in a namespace URI per the BracedURILiteral normalization rules:
    /// strip leading/trailing whitespace and collapse internal runs to a single space.
    /// </summary>
    internal static string CollapseWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length);
        bool inWs = false;
        bool started = false;
        foreach (var ch in s)
        {
            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                if (started) inWs = true;
                continue;
            }
            if (inWs) { sb.Append(' '); inWs = false; }
            sb.Append(ch);
            started = true;
        }
        return sb.ToString();
    }
}

/// <summary>
/// Computed attribute constructor operator — attribute { nameExpr } { valueExpr }.
/// The attribute name is computed at runtime from an expression.
/// </summary>
public sealed class ComputedAttributeConstructorOperator : PhysicalOperator
{
    public required PhysicalOperator NameOperator { get; init; }
    public required PhysicalOperator ValueOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate name
        string localName = "";
        string? prefix = null;
        int nameCount = 0;

        string? expandedNs = null;
        object? firstName = null;
        await foreach (var nameResult in NameOperator.ExecuteAsync(context))
        {
            nameCount++;
            if (nameCount > 1)
                throw new XQueryRuntimeException("XPTY0004",
                    "Attribute name must be a single atomic value, got a sequence");
            firstName = nameResult;
        }
        if (nameCount == 0)
            throw new XQueryRuntimeException("XPTY0004",
                "Attribute name cannot be an empty sequence");

        if (firstName is QName qn)
        {
            localName = qn.LocalName;
            prefix = qn.Prefix;
            expandedNs = qn.ResolvedNamespace;
        }
        else
        {
            var nameVal = (context.AtomizeWithNodes(firstName)?.ToString() ?? "").Trim();
            // Handle EQName: Q{uri}local
            if (nameVal.StartsWith("Q{", StringComparison.Ordinal))
            {
                // Find the closing '}' that separates namespace URI from local name.
                // The URI may contain '}' via resolved character references, so search
                // from the end for the last '}' followed by a valid NCName start char.
                int closeBrace = -1;
                for (int i = nameVal.Length - 1; i >= 2; i--)
                {
                    if (nameVal[i] == '}' && i + 1 < nameVal.Length
                        && ComputedElementConstructorOperator.IsValidNCNameStart(nameVal[i + 1]))
                    {
                        closeBrace = i;
                        break;
                    }
                }
                if (closeBrace < 0)
                    closeBrace = nameVal.IndexOf('}', 2);
                if (closeBrace > 1)
                {
                    var rawUri = nameVal[2..closeBrace];
                    if (rawUri.Contains('{') || rawUri.Contains('}'))
                        throw new XQueryRuntimeException("XQDY0074",
                            $"'{nameVal}' is not a valid expanded QName for an attribute: namespace URI contains '{{' or '}}'");
                    expandedNs = ComputedElementConstructorOperator.CollapseWhitespace(rawUri);
                    localName = nameVal[(closeBrace + 1)..];
                }
                else localName = nameVal;
            }
            else if (nameVal.Contains(':'))
            {
                var parts = nameVal.Split(':', 2);
                prefix = parts[0];
                localName = parts[1];
                if (prefix == "xml")
                    expandedNs = "http://www.w3.org/XML/1998/namespace";
                else if (prefix == "xmlns")
                    throw new XQueryRuntimeException("XQDY0044",
                        "Computed attribute names with prefix 'xmlns' are not allowed");
                else if (context.PrefixNamespaceBindings?.TryGetValue(prefix, out var nsUri) == true)
                    expandedNs = nsUri;
                else if (ComputedElementConstructorOperator.DefaultPrefixBindings.TryGetValue(prefix, out var defaultNsUri))
                    expandedNs = defaultNsUri;
                else
                    throw new XQueryRuntimeException("XQDY0074",
                        $"Namespace prefix '{prefix}' has not been declared");
            }
            else
            {
                localName = nameVal;
            }
        }

        // XQDY0074: localName must be a valid NCName
        if (!IsValidNCName(localName))
            throw new XQueryRuntimeException("XQDY0074",
                $"'{localName}' is not a valid NCName for an attribute");
        if (prefix != null && !IsValidNCName(prefix))
            throw new XQueryRuntimeException("XQDY0074",
                $"'{prefix}' is not a valid NCName for a prefix");

        // XQDY0044: Computed attribute cannot have name 'xmlns' (with no namespace)
        if (localName == "xmlns" && string.IsNullOrEmpty(prefix))
            throw new XQueryRuntimeException("XQDY0044",
                "The computed attribute name 'xmlns' is not allowed");
        // XQDY0044: Computed attribute cannot have prefix 'xmlns'
        if (prefix == "xmlns")
            throw new XQueryRuntimeException("XQDY0044",
                "Computed attribute names with prefix 'xmlns' are not allowed");
        // XQDY0044: Cannot be in the xmlns namespace (from fn:QName runtime namespace)
        if (expandedNs == "http://www.w3.org/2000/xmlns/")
            throw new XQueryRuntimeException("XQDY0044",
                "Computed attribute name cannot be in the 'http://www.w3.org/2000/xmlns/' namespace");
        // Prefix 'xml' must map to the XML namespace, and vice versa
        if (prefix == "xml" && expandedNs != null && expandedNs != "http://www.w3.org/XML/1998/namespace")
            throw new XQueryRuntimeException("XQDY0044",
                "Prefix 'xml' must be bound to 'http://www.w3.org/XML/1998/namespace'");
        if (expandedNs == "http://www.w3.org/XML/1998/namespace")
        {
            // Auto-assign xml prefix when no prefix given, error if wrong prefix
            if (string.IsNullOrEmpty(prefix)) prefix = "xml";
            else if (prefix != "xml")
                throw new XQueryRuntimeException("XQDY0044",
                    "Only prefix 'xml' can be bound to 'http://www.w3.org/XML/1998/namespace'");
        }

        // Auto-generate a prefix when namespace URI is present but no prefix was given.
        // Attributes with a namespace URI MUST have a prefix (unlike elements which can use default NS).
        if (!string.IsNullOrEmpty(expandedNs) && string.IsNullOrEmpty(prefix)
            && expandedNs != "http://www.w3.org/XML/1998/namespace")
        {
            // Try to find an existing prefix for this namespace in scope
            if (context.PrefixNamespaceBindings != null)
            {
                foreach (var (p, ns) in context.PrefixNamespaceBindings)
                {
                    if (ns == expandedNs && !string.IsNullOrEmpty(p))
                    {
                        prefix = p;
                        break;
                    }
                }
            }
            // If no existing prefix found, generate one
            if (string.IsNullOrEmpty(prefix))
            {
                // Generate ns0, ns1, ns2... until we find an unused prefix
                for (int i = 0; ; i++)
                {
                    var candidate = $"ns{i}";
                    if (context.PrefixNamespaceBindings == null
                        || !context.PrefixNamespaceBindings.ContainsKey(candidate))
                    {
                        prefix = candidate;
                        break;
                    }
                }
            }
        }

        var name = new QName(NamespaceId.None, localName, prefix) { ExpandedNamespace = expandedNs };
        var delegateOp = new AttributeConstructorOperator
        {
            Name = name,
            ValueOperator = ValueOperator
        };

        await foreach (var result in delegateOp.ExecuteAsync(context))
        {
            yield return result;
        }
    }

    private static bool IsValidNCName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var first = name[0];
        if (first != '_' && !char.IsLetter(first)) return false;
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_'
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.SpacingCombiningMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.EnclosingMark)
                return false;
        }
        return true;
    }
}

/// <summary>
/// Range expression: start to end → yields integers.
/// </summary>
public sealed class RangeOperator : PhysicalOperator
{
    public required PhysicalOperator Start { get; init; }
    public required PhysicalOperator End { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? startVal = null, endVal = null;
        await foreach (var item in Start.ExecuteAsync(context))
        { startVal = item; break; }
        await foreach (var item in End.ExecuteAsync(context))
        { endVal = item; break; }

        if (startVal == null || endVal == null)
            yield break;

        startVal = context.AtomizeWithNodes(startVal);
        endVal = context.AtomizeWithNodes(endVal);
        // xs:untypedAtomic is cast to xs:integer; other non-integer types are XPTY0004
        if (startVal is Xdm.XsUntypedAtomic sua) startVal = long.Parse(sua.Value);
        if (endVal is Xdm.XsUntypedAtomic eua) endVal = long.Parse(eua.Value);
        if (startVal is double or float or decimal)
            throw new XQueryRuntimeException("XPTY0004", "Range expression requires xs:integer operands");
        if (endVal is double or float or decimal)
            throw new XQueryRuntimeException("XPTY0004", "Range expression requires xs:integer operands");
        // Support BigInteger ranges for values beyond long range
        if (startVal is BigInteger || endVal is BigInteger)
        {
            var sBig = startVal is BigInteger sb2 ? sb2 : new BigInteger(Convert.ToInt64(startVal));
            var eBig = endVal is BigInteger eb2 ? eb2 : new BigInteger(Convert.ToInt64(endVal));
            for (var i = sBig; i <= eBig; i++)
            {
                if ((i - sBig) % 1024 == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                yield return i >= long.MinValue && i <= long.MaxValue ? (object)(long)i : i;
            }
        }
        else
        {
            var s = Convert.ToInt64(startVal);
            var e = Convert.ToInt64(endVal);
            for (var i = s; i <= e; i++)
            {
                if ((i - s) % 1024 == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                yield return i;
            }
        }
    }
}

/// <summary>
/// Cast expression: expr cast as type.
/// </summary>
public sealed class CastOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required XdmSequenceType TargetType { get; init; }
    public bool OperandIsStringLiteral { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // XQuery 3.1 §19.1: cast expression operand must be a singleton (or empty with ?)
        object? value = null;
        int count = 0;
        await foreach (var item in Operand.ExecuteAsync(context))
        {
            count++;
            if (count == 1)
                value = item;
            else
                throw new XQueryRuntimeException("XPTY0004",
                    "Cast expression operand is not a single atomic value");
        }

        if (count == 0)
        {
            if (TargetType.Occurrence == Occurrence.ZeroOrOne)
                yield break;
            throw new XQueryRuntimeException("XPTY0004",
                "Empty sequence cannot be cast to non-optional type");
        }

        // XPath/XQuery 3.1 §19.1.1: the operand is first atomized with fn:data(). For a node,
        // this yields the typed value (xs:untypedAtomic for untyped elements/attrs). Casting to a
        // namespace-sensitive type (xs:QName, xs:NOTATION) from a node IS allowed inside an explicit
        // cast expression in XQuery 3.0+ (see bug 16089), but NOT as an implicit argument coercion
        // (that remains XPTY0117 — see CastAsNamespaceSensitiveType tests).
        value = QueryExecutionContext.AtomizeTyped(value);

        // XQuery §19.1: cast-to-xs:QName requires operand of static type xs:string/xs:untypedAtomic/xs:QName
        if (TargetType.ItemType == ItemType.QName
            && value is not (string or Xdm.XsUntypedAtomic or PhoenixmlDb.Core.QName))
            throw new XQueryRuntimeException("XPTY0004",
                "cast as xs:QName requires an xs:string, xs:untypedAtomic, or xs:QName operand");

        // XQuery 3.0+ relaxed the XQuery 1.0 rule that required a string literal operand for
        // cast as xs:QName (see QT3 test CastExpr K2-CastAs-32 and bug 16059). Per the current
        // spec, casting a computed string to xs:QName is permitted — the cast proceeds at runtime
        // so long as the value is a valid lexical QName.

        // Special handling for cast as xs:QName: resolve prefix using in-scope namespace bindings
        // from the execution context (including xmlns: from enclosing direct element constructors).
        if (TargetType.ItemType == ItemType.QName && value is (string or Xdm.XsUntypedAtomic))
        {
            var s = (value is Xdm.XsUntypedAtomic ua ? ua.Value : (string)value).Trim();
            if (s.Length == 0)
                throw new XQueryRuntimeException("FORG0001", "Cannot cast empty string to xs:QName");
            var colonIdx = s.IndexOf(':', StringComparison.Ordinal);
            if (colonIdx > 0)
            {
                var prefix = s[..colonIdx];
                var localName = s[(colonIdx + 1)..];
                if (!TypeCastHelper.IsValidNCNameLex(prefix) || !TypeCastHelper.IsValidNCNameLex(localName))
                    throw new XQueryRuntimeException("FORG0001",
                        $"'{s}' is not a valid lexical xs:QName");
                string? nsUri = null;
                if (context.PrefixNamespaceBindings != null)
                    context.PrefixNamespaceBindings.TryGetValue(prefix, out nsUri);
                // Built-in predeclared namespace prefixes
                if (string.IsNullOrEmpty(nsUri))
                {
                    nsUri = prefix switch
                    {
                        "fn" => "http://www.w3.org/2005/xpath-functions",
                        "xs" => "http://www.w3.org/2001/XMLSchema",
                        "xsi" => "http://www.w3.org/2001/XMLSchema-instance",
                        "math" => "http://www.w3.org/2005/xpath-functions/math",
                        "map" => "http://www.w3.org/2005/xpath-functions/map",
                        "array" => "http://www.w3.org/2005/xpath-functions/array",
                        "err" => "http://www.w3.org/2005/xqt-errors",
                        "local" => "http://www.w3.org/2005/xquery-local-functions",
                        "xml" => "http://www.w3.org/XML/1998/namespace",
                        _ => null
                    };
                }
                if (string.IsNullOrEmpty(nsUri))
                    throw new XQueryRuntimeException("FONS0004",
                        $"No namespace binding for prefix '{prefix}' in cast as xs:QName");
                var nsId = new Core.NamespaceId((uint)Math.Abs(nsUri.GetHashCode()));
                yield return new Core.QName(nsId, localName, prefix) { RuntimeNamespace = nsUri };
            }
            else
            {
                if (!TypeCastHelper.IsValidNCNameLex(s))
                    throw new XQueryRuntimeException("FORG0001",
                        $"'{s}' is not a valid lexical xs:QName");
                yield return new Core.QName(Core.NamespaceId.None, s);
            }
            yield break;
        }

        var result = TypeCastHelper.CastValue(value, TargetType.ItemType);
        // Validate integer subtype ranges (long, int, unsignedLong, etc. — xs:integer has no bound).
        // Use LocalTypeName so xs:int (prefixed) and int (unprefixed via xpath-default-namespace)
        // both validate; UnprefixedTypeName is reserved for the XSLT XPST0051 contract.
        var typeLocalName = TargetType.LocalTypeName ?? TargetType.UnprefixedTypeName;
        if (typeLocalName != null && result is long l)
            TypeCastHelper.ValidateIntegerSubtype(l, typeLocalName);
        else if (typeLocalName != null && result is BigInteger bi)
            TypeCastHelper.ValidateIntegerSubtype(bi, typeLocalName);
        // Normalize/validate xs:string derived subtypes
        if (TargetType.ItemType == ItemType.String && typeLocalName != null)
        {
            var strVal = result is Xdm.XsTypedString ts ? ts.Value : result as string;
            if (strVal != null)
                result = TypeCastHelper.NormalizeStringSubtype(strVal, typeLocalName);
        }
        yield return result;
    }
}

/// <summary>
/// Castable expression: expr castable as type → boolean.
/// </summary>
public sealed class CastableOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required XdmSequenceType TargetType { get; init; }
    public bool OperandIsStringLiteral { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? value = null;
        int itemCount = 0;
        await foreach (var item in Operand.ExecuteAsync(context))
        {
            value = item;
            itemCount++;
            if (itemCount > 1)
                break; // More than one item — not castable
        }

        if (itemCount == 0)
        {
            yield return TargetType.Occurrence == Occurrence.ZeroOrOne;
            yield break;
        }

        // castable as allows at most one item; sequences of length > 1 are never castable
        if (itemCount > 1)
        {
            yield return false;
            yield break;
        }

        // XQuery 3.0+ permits castable as xs:QName against computed strings (see QT3
        // CastableExpr and CastExpr test suites, bug 16059). The castable result is
        // determined purely by whether the lexical form is a valid xs:QName at runtime.

        bool castable;
        try
        {
            var castResult = TypeCastHelper.CastValue(value, TargetType.ItemType);
            // Use LocalTypeName (set regardless of prefix) for derived-type checks.
            var localName = TargetType.LocalTypeName ?? TargetType.UnprefixedTypeName;
            if (localName != null && castResult is long l)
                TypeCastHelper.ValidateIntegerSubtype(l, localName);
            else if (localName != null && castResult is BigInteger bi)
                TypeCastHelper.ValidateIntegerSubtype(bi, localName);
            if (TargetType.ItemType == ItemType.String && localName != null)
            {
                var cs = castResult is Xdm.XsTypedString ts2 ? ts2.Value : castResult as string;
                if (cs != null)
                    TypeCastHelper.NormalizeStringSubtype(cs, localName);
            }
            castable = true;
        }
        catch
        {
            castable = false;
        }
        yield return castable;
    }
}

/// <summary>
/// Instance of expression: expr instance of type → boolean.
/// </summary>
public sealed class InstanceOfOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var items = new List<object?>();
        await foreach (var item in Operand.ExecuteAsync(context))
            items.Add(item);

        yield return TypeCastHelper.MatchesType(items, TargetType, context.SchemaProvider);
    }
}

/// <summary>
/// Treat expression: runtime type assertion that raises XPDY0050 on mismatch.
/// Unlike cast, treat does not convert values — it only checks that they already match.
/// </summary>
public sealed class TreatOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var items = new List<object?>();
        await foreach (var item in Operand.ExecuteAsync(context))
            items.Add(item);

        // Check cardinality
        var count = items.Count;
        switch (TargetType.Occurrence)
        {
            case Occurrence.ExactlyOne when count != 1:
                throw new XQueryRuntimeException("XPDY0050",
                    $"Required cardinality of value treated as {TargetType} is exactly one, but the sequence has {count} item(s)");
            case Occurrence.ZeroOrOne when count > 1:
                throw new XQueryRuntimeException("XPDY0050",
                    $"Required cardinality of value treated as {TargetType} is zero or one, but the sequence has {count} items");
            case Occurrence.OneOrMore when count < 1:
                throw new XQueryRuntimeException("XPDY0050",
                    $"Required cardinality of value treated as {TargetType} is one or more, but the sequence is empty");
        }

        // Check item types (unless target is item())
        if (TargetType.ItemType != ItemType.Item)
        {
            foreach (var item in items)
            {
                if (item != null && !TypeCastHelper.MatchesSequenceItemType(item, TargetType, context.SchemaProvider))
                    throw new XQueryRuntimeException("XPDY0050",
                        $"An item in the sequence does not match the required type {TargetType}: got {item.GetType().Name}");

                // Check derived integer subtype range (e.g., treat as xs:negativeInteger)
                if (item != null && TargetType.DerivedIntegerType != null && TargetType.ItemType == ItemType.Integer)
                {
                    if (!TypeCastHelper.MatchesDerivedIntegerRange(item, TargetType.DerivedIntegerType))
                        throw new XQueryRuntimeException("XPDY0050",
                            $"An item in the sequence does not match the required type {TargetType}: value {item} is out of range for xs:{TargetType.DerivedIntegerType}");
                }
            }
        }

        foreach (var item in items)
            yield return item;
    }
}

/// <summary>
/// String concatenation: a || b || c
/// </summary>
public sealed class StringConcatOperator : PhysicalOperator
{
    public required IReadOnlyList<PhysicalOperator> Operands { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var parts = new List<string>();
        foreach (var op in Operands)
        {
            object? val = null;
            await foreach (var item in op.ExecuteAsync(context))
            { val = item; break; }
            parts.Add(Functions.ConcatFunction.XQueryStringValue(val));
        }
        yield return string.Concat(parts);
    }
}

/// <summary>
/// Quantified expression: some/every $x in ... satisfies ...
/// </summary>
public sealed class QuantifiedOperator : PhysicalOperator
{
    public required Quantifier Quantifier { get; init; }
    public required IReadOnlyList<QuantifiedBindingOperator> Bindings { get; init; }
    public required PhysicalOperator Satisfies { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var result = await EvaluateBindingsAsync(context, 0);
        yield return result;
    }

    private async Task<bool> EvaluateBindingsAsync(QueryExecutionContext context, int index)
    {
        if (index >= Bindings.Count)
        {
            // All bindings established, evaluate satisfies
            // Collect all items for proper EBV (multi-item sequences → FORG0006)
            var satItems = new List<object?>();
            await foreach (var item in Satisfies.ExecuteAsync(context))
                satItems.Add(item);
            object? satVal = satItems.Count == 0 ? null
                : satItems.Count == 1 ? satItems[0]
                : satItems.ToArray();
            return QueryExecutionContext.EffectiveBooleanValue(satVal);
        }

        var binding = Bindings[index];
        var items = new List<object?>();
        await foreach (var item in binding.InputOperator.ExecuteAsync(context))
            items.Add(item);

        // Enforce type declaration on each bound item (XQuery §3.12.1)
        void CheckType(object? item)
        {
            if (binding.TypeDeclaration is { } td)
            {
                if (item != null && !TypeCastHelper.MatchesItemType(item, td.ItemType))
                    throw new XQueryRuntimeException("XPTY0004",
                        $"quantified ${binding.Variable.LocalName}: value does not match declared type {td}");
            }
        }

        if (Quantifier == Quantifier.Some)
        {
            foreach (var item in items)
            {
                CheckType(item);
                context.PushScope();
                context.BindVariable(binding.Variable, item);
                try
                {
                    if (await EvaluateBindingsAsync(context, index + 1))
                        return true;
                }
                finally { context.PopScope(); }
            }
            return false;
        }
        else // Every
        {
            foreach (var item in items)
            {
                CheckType(item);
                context.PushScope();
                context.BindVariable(binding.Variable, item);
                try
                {
                    if (!await EvaluateBindingsAsync(context, index + 1))
                        return false;
                }
                finally { context.PopScope(); }
            }
            return true;
        }
    }
}

/// <summary>
/// Quantified binding operator.
/// </summary>
public sealed class QuantifiedBindingOperator
{
    public required QName Variable { get; init; }
    public required PhysicalOperator InputOperator { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
}

/// <summary>
/// Try-catch expression.
/// </summary>
public sealed class TryCatchOperator : PhysicalOperator
{
    public required PhysicalOperator TryOperator { get; init; }
    public required IReadOnlyList<CatchClauseOperator> CatchClauses { get; init; }
    public NamespaceId ErrorNamespaceId { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        List<object?> results;
        try
        {
            results = new List<object?>();
            await foreach (var item in TryOperator.ExecuteAsync(context))
                results.Add(item);
        }
        catch (XQueryRuntimeException ex)
        {
            results = await ExecuteCatchAsync(ex, context);
        }
        catch (Functions.XQueryException ex)
        {
            // fn:error() throws XQueryException — wrap and catch
            var wrapped = new XQueryRuntimeException(ex.ErrorCode, ex.Message) { ErrorNamespaceUri = ex.ErrorNamespaceUri, ErrorPrefix = ex.ErrorPrefix, ErrorValue = ex.ErrorValue };
            results = await ExecuteCatchAsync(wrapped, context);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Catch .NET runtime errors (NullRef, InvalidCast, etc.) and wrap as XPDY0002/FOER0000
            var errorCode = ex switch
            {
                NullReferenceException => "XPDY0002",
                InvalidCastException => "XPTY0004",
                ArgumentException => "FOER0000",
                OverflowException => "FOAR0002",
                _ => "FOER0000"
            };
            var wrapped = new XQueryRuntimeException(errorCode, ex.Message);
            results = await ExecuteCatchAsync(wrapped, context);
        }

        foreach (var item in results)
            yield return item;
    }

    private async Task<List<object?>> ExecuteCatchAsync(XQueryRuntimeException ex, QueryExecutionContext context)
    {
        foreach (var clause in CatchClauses)
        {
            if (clause.Matches(ex.ErrorCode, ex.ErrorNamespaceUri))
            {
                var catchResults = new List<object?>();
                context.PushScope();
                // Bind err:* implicit variables per XQuery 3.1 §3.15.1
                // Variable names must use the actual err namespace ID so runtime lookups match
                // the namespace-resolved QNames produced by NamespaceResolver.
                var errNsId = ErrorNamespaceId;
                var errCode = ex.ErrorCode ?? "FOER0000";
                // The value of $err:code is a QName — use the actual namespace URI
                // so fn:namespace-uri-from-QName($err:code) works
                var errNsUri = ex.ErrorNamespaceUri ?? "http://www.w3.org/2005/xqt-errors";
                var errPrefix = ex.ErrorPrefix ?? "err";
                var errCodeQName = new QName(ErrorNamespaceId, errCode, errPrefix)
                    { RuntimeNamespace = errNsUri };
                context.BindVariable(new QName(errNsId, "code", "err"), errCodeQName);
                context.BindVariable(new QName(errNsId, "description", "err"), ex.Message ?? "");
                context.BindVariable(new QName(errNsId, "value", "err"), ex.ErrorValue);
                context.BindVariable(new QName(errNsId, "module", "err"), "");
                context.BindVariable(new QName(errNsId, "line-number", "err"), 0L);
                context.BindVariable(new QName(errNsId, "column-number", "err"), 0L);
                context.BindVariable(new QName(errNsId, "additional", "err"), null);
                try
                {
                    await foreach (var item in clause.ResultOperator.ExecuteAsync(context))
                        catchResults.Add(item);
                }
                finally { context.PopScope(); }
                return catchResults;
            }
        }
        throw ex; // No matching catch clause
    }
}

/// <summary>
/// Catch clause operator.
/// </summary>
public sealed class CatchClauseOperator
{
    public required IReadOnlyList<NameTest> ErrorCodes { get; init; }
    public required PhysicalOperator ResultOperator { get; init; }

    public bool Matches(string errorCode) => Matches(errorCode, null);

    public bool Matches(string errorCode, string? errorNamespaceUri)
    {
        // Error codes like "FOAR0001" live in the err: namespace by default.
        // User-raised errors via fn:error(xs:QName) carry an explicit namespace.
        const string ErrNs = "http://www.w3.org/2005/xqt-errors";
        var actualNs = errorNamespaceUri ?? ErrNs;
        foreach (var test in ErrorCodes)
        {
            bool localMatch = test.LocalName == "*" || test.LocalName == errorCode;

            bool nsMatch;
            if (test.Prefix == null && test.NamespaceUri == null && test.LocalName == "*" && !test.IsNamespaceWildcard)
                nsMatch = true;                                       // catch *
            else if (test.IsNamespaceWildcard || test.NamespaceUri == "*")
                nsMatch = true;                                       // catch *:local
            else if (test.NamespaceUri != null)
                nsMatch = test.NamespaceUri == actualNs;              // catch Q{uri}...
            else if (test.Prefix != null)
                nsMatch = GetNamespaceForPrefix(test.Prefix) == actualNs;
            else
                nsMatch = actualNs == ErrNs;                          // unprefixed → err namespace

            if (localMatch && nsMatch)
                return true;
        }
        return false;
    }

    private static string? GetNamespaceForPrefix(string? prefix)
    {
        return prefix switch
        {
            "err" => "http://www.w3.org/2005/xqt-errors",
            "xs" => "http://www.w3.org/2001/XMLSchema",
            "fn" => "http://www.w3.org/2005/xpath-functions",
            "local" => "http://www.w3.org/2005/xquery-local-functions",
            "map" => "http://www.w3.org/2005/xpath-functions/map",
            "array" => "http://www.w3.org/2005/xpath-functions/array",
            _ => null
        };
    }
}

/// <summary>
/// Simple map expression: left ! right
/// </summary>
public sealed class SimpleMapOperator : PhysicalOperator
{
    public required PhysicalOperator Left { get; init; }
    public required PhysicalOperator Right { get; init; }

    /// <summary>
    /// When false, the Right expression does not reference position() or last(),
    /// so we can stream items without materializing the full Left sequence.
    /// </summary>
    public bool RequiresPositionalAccess { get; init; } = true;

    /// <summary>
    /// When true, this is a path step (/) rather than a simple map (!),
    /// and non-node context items must raise XPTY0019.
    /// </summary>
    public bool IsPathStep { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        if (!RequiresPositionalAccess)
        {
            var position = 0;
            await foreach (var item in Left.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (IsPathStep && item is not XdmNode)
                    throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0019",
                        "The context item for an axis step is not a node");
                position++;
                context.PushContextItem(item, position, -1);
                try
                {
                    await foreach (var result in Right.ExecuteAsync(context))
                        yield return result;
                }
                finally { context.PopContextItem(); }
            }
            yield break;
        }

        var items = new List<object?>();
        await foreach (var item in Left.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            items.Add(item);
            context.CheckMaterializationLimit(items.Count);
        }

        {
            var position = 0;
            foreach (var item in items)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (IsPathStep && item is not XdmNode)
                    throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0019",
                        "The context item for an axis step is not a node");
                position++;
                context.PushContextItem(item, position, items.Count);
                try
                {
                    await foreach (var result in Right.ExecuteAsync(context))
                        yield return result;
                }
                finally { context.PopContextItem(); }
            }
        }
    }
}

/// <summary>
/// Switch expression.
/// </summary>
public sealed class SwitchOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required IReadOnlyList<SwitchCaseOperator> Cases { get; init; }
    public required PhysicalOperator Default { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? operandVal = null;
        int operandCount = 0;
        await foreach (var item in Operand.ExecuteAsync(context))
        {
            operandVal = item;
            operandCount++;
            if (operandCount > 1)
                throw new XQueryRuntimeException("XPTY0004",
                    "Switch operand must be a single atomic value, got a sequence");
        }
        // Atomize the operand
        operandVal = QueryExecutionContext.Atomize(operandVal);

        foreach (var @case in Cases)
        {
            foreach (var valueOp in @case.Values)
            {
                object? caseVal = null;
                int caseCount = 0;
                await foreach (var item in valueOp.ExecuteAsync(context))
                {
                    caseVal = item;
                    caseCount++;
                    if (caseCount > 1)
                        throw new XQueryRuntimeException("XPTY0004",
                            "Switch case operand must be a single atomic value, got a sequence");
                }
                // Atomize the case value
                caseVal = QueryExecutionContext.Atomize(caseVal);

                // Per XQuery 3.1 §3.14: switch comparison uses eq semantics
                // except NaN matches NaN and () matches ()
                bool matches = false;
                if (operandVal == null && caseVal == null)
                    matches = true;
                else if (operandVal is double dOp && double.IsNaN(dOp)
                         && caseVal is double dCase && double.IsNaN(dCase))
                    matches = true;
                else if (operandVal is float fOp && float.IsNaN(fOp)
                         && caseVal is float fCase && float.IsNaN(fCase))
                    matches = true;
                else if (operandVal is double dOp2 && double.IsNaN(dOp2)
                         && caseVal is float fCase2 && float.IsNaN(fCase2))
                    matches = true;
                else if (operandVal is float fOp2 && float.IsNaN(fOp2)
                         && caseVal is double dCase2 && double.IsNaN(dCase2))
                    matches = true;
                else if (operandVal != null && caseVal != null)
                    matches = TypeCastHelper.DeepEquals(operandVal, caseVal, nodeProvider: context.NodeProvider);

                if (matches)
                {
                    await foreach (var result in @case.Result.ExecuteAsync(context))
                        yield return result;
                    yield break;
                }
            }
        }

        // No case matched, use default
        await foreach (var result in Default.ExecuteAsync(context))
            yield return result;
    }
}

/// <summary>
/// Switch case operator.
/// </summary>
public sealed class SwitchCaseOperator
{
    public required IReadOnlyList<PhysicalOperator> Values { get; init; }
    public required PhysicalOperator Result { get; init; }
}

/// <summary>
/// Typeswitch expression.
/// </summary>
public sealed class TypeswitchOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required IReadOnlyList<TypeswitchCaseOperator> Cases { get; init; }
    public QName? DefaultVariable { get; init; }
    public required PhysicalOperator DefaultResult { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var items = new List<object?>();
        await foreach (var item in Operand.ExecuteAsync(context))
            items.Add(item);

        object? operandVal = items.Count switch
        {
            0 => null,
            1 => items[0],
            _ => items.ToArray()
        };

        foreach (var @case in Cases)
        {
            foreach (var type in @case.Types)
            {
                if (TypeCastHelper.MatchesType(items, type))
                {
                    context.PushScope();
                    if (@case.Variable.HasValue)
                        context.BindVariable(@case.Variable.Value, operandVal);
                    try
                    {
                        await foreach (var result in @case.Result.ExecuteAsync(context))
                            yield return result;
                    }
                    finally { context.PopScope(); }
                    yield break;
                }
            }
        }

        // Default case
        context.PushScope();
        if (DefaultVariable.HasValue)
            context.BindVariable(DefaultVariable.Value, operandVal);
        try
        {
            await foreach (var result in DefaultResult.ExecuteAsync(context))
                yield return result;
        }
        finally { context.PopScope(); }
    }
}

/// <summary>
/// Typeswitch case operator.
/// </summary>
public sealed class TypeswitchCaseOperator
{
    public QName? Variable { get; init; }
    public required IReadOnlyList<XdmSequenceType> Types { get; init; }
    public required PhysicalOperator Result { get; init; }
}

/// <summary>
/// Map constructor: map { key: value, ... }
/// </summary>
public sealed class MapConstructorOperator : PhysicalOperator
{
    public required IReadOnlyList<MapEntryOperator> Entries { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var map = new Dictionary<object, object?>(XdmMapKeyComparer.Instance);
        foreach (var entry in Entries)
        {
            // Collect all key items to check for singleton.
            // Per XQuery 3.1 §3.11.1: the key expression is atomized.
            var keyItems = new List<object?>();
            await foreach (var item in entry.Key.ExecuteAsync(context))
            {
                var atomized = context.AtomizeWithNodes(item);
                if (atomized is object?[] atomizedSeq)
                {
                    foreach (var a in atomizedSeq)
                        keyItems.Add(a);
                }
                else
                    keyItems.Add(atomized);
            }
            if (keyItems.Count == 0)
                throw new XQueryRuntimeException("XPTY0004",
                    "Map key must be a single atomic value, got an empty sequence");
            if (keyItems.Count > 1)
                throw new XQueryRuntimeException("XPTY0004",
                    "Map key must be a single atomic value, got a sequence of " + keyItems.Count + " items");
            var key = keyItems[0];
            // Collect all value items — map values are sequences per XPath spec
            var valueItems = new List<object?>();
            await foreach (var item in entry.Value.ExecuteAsync(context))
                valueItems.Add(item);
            var value = valueItems.Count == 1 ? valueItems[0] : valueItems.Count == 0 ? null : valueItems.ToArray();
            if (key != null)
            {
                if (map.ContainsKey(key))
                    throw new XQueryRuntimeException("XQDY0137",
                        $"Duplicate key '{key}' in map constructor");
                map[key] = value;
            }
        }
        yield return map;
    }
}

/// <summary>
/// XDM-aware key equality comparer for map keys.
/// Handles cross-type numeric equality (int/long/float/double/decimal),
/// NaN=NaN, INF=INF across float/double, and timezone-normalized time equality.
/// </summary>
internal sealed class XdmMapKeyComparer : IEqualityComparer<object>
{
    public static readonly XdmMapKeyComparer Instance = new();

    public new bool Equals(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;
        if (x.Equals(y)) return true;

        // Cross-type numeric comparison — per op:same-key, two numerics are equal iff
        // they represent exactly the same mathematical value. Double-coercion is too
        // loose: xs:decimal("1.1") and xs:double("1.1") are NOT same-key because
        // xs:double("1.1") ≠ 11/10 exactly. (same-key-006, same-key-007)
        if (IsNumeric(x) && IsNumeric(y))
        {
            // Handle NaN: NaN equals NaN for map key purposes
            if (IsNaN(x) && IsNaN(y)) return true;
            // Handle Infinity
            if (IsPositiveInfinity(x) && IsPositiveInfinity(y)) return true;
            if (IsNegativeInfinity(x) && IsNegativeInfinity(y)) return true;
            // If either is NaN/Inf but not both, they're unequal
            if (IsNaN(x) || IsNaN(y) || IsPositiveInfinity(x) || IsPositiveInfinity(y)
                || IsNegativeInfinity(x) || IsNegativeInfinity(y))
                return false;
            return ExactNumericEquals(x, y);
        }

        // Date/time comparison for op:same-key semantics (per XPath 3.1 §17.1.1):
        //   use a fixed implicit timezone of UTC (Z) for values that lack a timezone,
        //   then compare as UTC instants. This differs from op:eq / distinct-values
        //   / group-by, which use the system's implicit timezone.
        if (x is Xdm.XsTime tx && y is Xdm.XsTime ty)
            return SameKeyTimeUtcTicks(tx) == SameKeyTimeUtcTicks(ty);
        if (x is Xdm.XsDateTime dtx && y is Xdm.XsDateTime dty)
            return SameKeyDateTimeUtcTicks(dtx) == SameKeyDateTimeUtcTicks(dty);
        if (x is Xdm.XsDate datex && y is Xdm.XsDate datey)
            return SameKeyDateUtcTicks(datex) == SameKeyDateUtcTicks(datey);

        // xs:gYear-family: map-key equality uses lexical (canonical) comparison without
        // applying implicit timezone — same rule as date/time above. The canonical lexical
        // form from our type constructors already normalizes the tz, so string equality
        // gives us the op:same-key semantics.
        if (x is Xdm.XsGYear gya && y is Xdm.XsGYear gyb) return gya.Value == gyb.Value;
        if (x is Xdm.XsGYearMonth gyma && y is Xdm.XsGYearMonth gymb) return gyma.Value == gymb.Value;
        if (x is Xdm.XsGMonth gma && y is Xdm.XsGMonth gmb) return gma.Value == gmb.Value;
        if (x is Xdm.XsGMonthDay gmda && y is Xdm.XsGMonthDay gmdb) return gmda.Value == gmdb.Value;
        if (x is Xdm.XsGDay gda && y is Xdm.XsGDay gdb) return gda.Value == gdb.Value;

        // xs:anyURI / xs:string cross-type
        var sx = x is Xdm.XsAnyUri ax ? ax.Value : x as string;
        var sy = y is Xdm.XsAnyUri ay ? ay.Value : y as string;
        if (sx != null && sy != null) return sx == sy;

        // xs:untypedAtomic / xs:string cross-type
        var ux = x is Xdm.XsUntypedAtomic uax ? uax.Value : x as string;
        var uy = y is Xdm.XsUntypedAtomic uay ? uay.Value : y as string;
        if (ux != null && uy != null) return ux == uy;

        // Duration cross-type
        if (IsDuration(x) && IsDuration(y))
        {
            var (xm, xt) = GetDurationComponents(x);
            var (ym, yt) = GetDurationComponents(y);
            return xm == ym && xt == yt;
        }

        // QName comparison
        if (x is QName qx && y is QName qy)
        {
            if (qx.LocalName != qy.LocalName) return false;
            var lUri = qx.ResolvedNamespace;
            var rUri = qy.ResolvedNamespace;
            if (lUri != null && rUri != null) return lUri == rUri;
            return qx.Namespace == qy.Namespace;
        }

        return false;
    }

    public int GetHashCode(object obj)
    {
        if (IsNumeric(obj))
        {
            if (IsNaN(obj)) return int.MinValue; // All NaN values have the same hash
            if (IsPositiveInfinity(obj)) return int.MaxValue;
            if (IsNegativeInfinity(obj)) return int.MaxValue - 1;
            // Hash by converting to double when the value is representable exactly there,
            // and falling back to obj.GetHashCode() otherwise. ExactNumericEquals below
            // handles the cases where values collide in hash but differ in exactness.
            // Integers, longs, and BigIntegers within long range hash to same double.
            try
            {
                var d = Convert.ToDouble(obj, System.Globalization.CultureInfo.InvariantCulture);
                // Only use double-hash when double conversion is exact for numeric comparison.
                // For decimals: round-trip through double to check exactness.
                if (obj is decimal dec)
                {
                    if ((decimal)d == dec) return d.GetHashCode();
                    // Non-representable: use decimal's own hash to avoid bucketing with double
                    return dec.GetHashCode();
                }
                return d.GetHashCode();
            }
            catch { return obj.GetHashCode(); }
        }
        // Use UTC-with-Z-default hash for date/time types so that same-key values
        // (equal as UTC instants when no-tz defaults to Z) hash consistently.
        if (obj is Xdm.XsTime xt)
            return SameKeyTimeUtcTicks(xt).GetHashCode();
        if (obj is Xdm.XsDateTime xdt)
            return SameKeyDateTimeUtcTicks(xdt).GetHashCode();
        if (obj is Xdm.XsDate xd)
            return SameKeyDateUtcTicks(xd).GetHashCode();
        // xs:anyURI and xs:string must share hash codes for cross-type lookup
        if (obj is Xdm.XsAnyUri au)
            return au.Value.GetHashCode();
        // xs:untypedAtomic and xs:string must share hash codes
        if (obj is Xdm.XsUntypedAtomic ua)
            return ua.Value.GetHashCode();
        // Duration types must share hash codes for cross-type lookup
        if (IsDuration(obj))
        {
            var (m, t) = GetDurationComponents(obj);
            return HashCode.Combine(m, t);
        }
        // QName: hash by local name + namespace
        if (obj is QName q)
            return HashCode.Combine(q.LocalName, q.ResolvedNamespace ?? q.Namespace.ToString());
        return obj.GetHashCode();
    }

    private static bool IsNumeric(object x)
        => x is int or long or double or float or decimal or System.Numerics.BigInteger;

    /// <summary>
    /// Same-key UTC ticks for xs:time: ticks − offset, using Z (zero offset) when
    /// the value has no explicit timezone, per XPath 3.1 §17.1.1.
    /// </summary>
    private static long SameKeyTimeUtcTicks(Xdm.XsTime t)
        => t.Time.Ticks - (t.Timezone ?? TimeSpan.Zero).Ticks;

    /// <summary>
    /// Same-key UTC ticks for xs:date: midnight UTC instant using Z when no timezone is set.
    /// </summary>
    private static long SameKeyDateUtcTicks(Xdm.XsDate d)
    {
        if (d.ExtendedYear.HasValue)
        {
            // Extended year values fall outside DateTime range — approximate ordering.
            return d.EffectiveYear * 365L * TimeSpan.TicksPerDay
                + d.Date.Month * 31L * TimeSpan.TicksPerDay
                + d.Date.Day * TimeSpan.TicksPerDay
                - (d.Timezone ?? TimeSpan.Zero).Ticks;
        }
        var dt = d.Date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(dt, d.Timezone ?? TimeSpan.Zero).UtcTicks;
    }

    /// <summary>
    /// Same-key UTC ticks for xs:dateTime: UTC instant using Z when no timezone is set.
    /// </summary>
    private static long SameKeyDateTimeUtcTicks(Xdm.XsDateTime dt)
    {
        if (dt.HasTimezone) return dt.Value.UtcTicks;
        // Re-interpret the local wall clock as UTC
        return new DateTimeOffset(dt.Value.DateTime, TimeSpan.Zero).UtcTicks;
    }

    /// <summary>
    /// Compares two numerics by their exact mathematical value per op:same-key semantics.
    /// Unlike the double-coerced equality used for fn:eq, this returns true only when the
    /// two values represent identical mathematical values. For example:
    ///   xs:decimal("1.1") and xs:double("1.1") → false (double(1.1) ≠ 11/10 exactly)
    ///   xs:integer(1) and xs:double(1.0)         → true
    ///   xs:decimal("1.0") and xs:integer(1)      → true
    /// </summary>
    private static bool ExactNumericEquals(object x, object y)
    {
        // Same CLR type: use direct equality
        if (x.GetType() == y.GetType())
            return x.Equals(y);

        // double vs float: both IEEE, promote float to double and compare as doubles
        if ((x is double or float) && (y is double or float))
        {
            var dx = Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture);
            var dy = Convert.ToDouble(y, System.Globalization.CultureInfo.InvariantCulture);
            return dx == dy;
        }

        // Pure integral types (int/long/BigInteger): compare via BigInteger
        if (IsIntegral(x) && IsIntegral(y))
            return ToBigInteger(x) == ToBigInteger(y);

        // Mixed integral and decimal: convert integral to decimal when in range
        if ((IsIntegral(x) && y is decimal dy2) || (x is decimal && IsIntegral(y)))
        {
            try
            {
                var xd = x is decimal xdec ? xdec : IntegralToDecimal(x);
                var yd = y is decimal ydec ? ydec : IntegralToDecimal(y);
                return xd == yd;
            }
            catch { return false; }
        }

        // Mixed with IEEE float/double: they're equal only if the float/double represents
        // an integer/decimal value EXACTLY. We check by round-tripping.
        if ((x is double or float) || (y is double or float))
        {
            var flt = (x is double or float) ? x : y;
            var other = (x is double or float) ? y : x;
            double fd = Convert.ToDouble(flt, System.Globalization.CultureInfo.InvariantCulture);
            if (double.IsNaN(fd) || double.IsInfinity(fd)) return false;

            if (IsIntegral(other))
            {
                // Integer vs double: equal iff double is finite and integer-valued and matches
                if (Math.Floor(fd) != fd) return false;
                try
                {
                    var otherBig = ToBigInteger(other);
                    var fltBig = new System.Numerics.BigInteger(fd);
                    return otherBig == fltBig;
                }
                catch { return false; }
            }
            if (other is decimal odec)
            {
                // Decimal vs double: compare EXACTLY by decomposing both to rational form.
                // The naive `(decimal)fd == odec` round-trip lies because .NET's cast rounds
                // the double's infinite binary fraction to 15 significant digits. For example,
                // (decimal)(double)1.1 yields exactly 1.1m even though the double is
                // 1.1000000000000000888... — they are NOT mathematically equal.
                return DoubleEqualsDecimalExactly(fd, odec);
            }
        }
        return false;
    }

    /// <summary>
    /// Exact mathematical equality between an IEEE 754 double and a .NET decimal.
    /// Decomposes each into a rational number (mantissa × 2^exp vs. unscaled × 10^-scale)
    /// then compares via BigInteger arithmetic — no lossy round-trip through either format.
    /// </summary>
    private static bool DoubleEqualsDecimalExactly(double d, decimal m)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return false;
        if (d == 0.0) return m == 0m;
        if (m == 0m) return false;

        // Sign check
        bool dNeg = double.IsNegative(d);
        bool mNeg = m < 0m;
        if (dNeg != mNeg) return false;

        // Decompose double: d = sign × mantissa × 2^exp
        long bits = BitConverter.DoubleToInt64Bits(d);
        long mantissaBits = bits & 0x000fffffffffffffL;
        int rawExp = (int)((bits >> 52) & 0x7ffL);
        long dMantissaL;
        int dExp;
        if (rawExp == 0)
        {
            // Subnormal: no implicit leading 1-bit
            dMantissaL = mantissaBits;
            dExp = -1074;
        }
        else
        {
            dMantissaL = mantissaBits | 0x0010000000000000L;
            dExp = rawExp - 1075;
        }
        var dMantissa = new System.Numerics.BigInteger(dMantissaL);

        // Decompose decimal: |m| = unscaled / 10^scale (unscaled is 96-bit unsigned)
        int[] bitsDec = decimal.GetBits(m);
        int scale = (bitsDec[3] >> 16) & 0xff;
        var lo = (uint)bitsDec[0];
        var mid = (uint)bitsDec[1];
        var hi = (uint)bitsDec[2];
        var unscaled = (new System.Numerics.BigInteger(hi) << 64)
                     + (new System.Numerics.BigInteger(mid) << 32)
                     + new System.Numerics.BigInteger(lo);

        // Equality: dMantissa × 2^dExp = unscaled / 10^scale
        //        ⇔ dMantissa × 2^(dExp+scale) × 5^scale = unscaled
        // Avoid negative bit-shifts by moving the negative-power side to the other term.
        int shift = dExp + scale;
        var fivePow = System.Numerics.BigInteger.Pow(5, scale);
        System.Numerics.BigInteger lhs, rhs;
        if (shift >= 0)
        {
            lhs = dMantissa * (System.Numerics.BigInteger.One << shift) * fivePow;
            rhs = unscaled;
        }
        else
        {
            lhs = dMantissa * fivePow;
            rhs = unscaled * (System.Numerics.BigInteger.One << (-shift));
        }
        return lhs == rhs;
    }

    private static bool IsIntegral(object x)
        => x is int or long or System.Numerics.BigInteger;

    private static System.Numerics.BigInteger ToBigInteger(object x) => x switch
    {
        int i => new System.Numerics.BigInteger(i),
        long l => new System.Numerics.BigInteger(l),
        System.Numerics.BigInteger b => b,
        _ => throw new ArgumentException($"Not an integral: {x.GetType()}")
    };

    private static decimal IntegralToDecimal(object x) => x switch
    {
        int i => (decimal)i,
        long l => (decimal)l,
        System.Numerics.BigInteger b => (decimal)b,
        _ => throw new ArgumentException($"Not an integral: {x.GetType()}")
    };

    private static bool IsNaN(object x)
        => x is double d && double.IsNaN(d) || x is float f && float.IsNaN(f);

    private static bool IsPositiveInfinity(object x)
        => x is double d && double.IsPositiveInfinity(d) || x is float f && float.IsPositiveInfinity(f);

    private static bool IsNegativeInfinity(object x)
        => x is double d && double.IsNegativeInfinity(d) || x is float f && float.IsNegativeInfinity(f);

    private static bool IsDuration(object x) =>
        x is Xdm.XsDuration or Xdm.YearMonthDuration or TimeSpan or Xdm.DayTimeDuration;

    private static (int months, long ticks) GetDurationComponents(object dur) => dur switch
    {
        Xdm.XsDuration d => (d.TotalMonths, d.DayTime.Ticks),
        Xdm.YearMonthDuration ymd => (ymd.TotalMonths, 0),
        TimeSpan ts => (0, ts.Ticks),
        Xdm.DayTimeDuration dtd => (0, (long)(dtd.TotalSeconds * TimeSpan.TicksPerSecond)),
        _ => (0, 0)
    };
}

/// <summary>
/// Map entry operator.
/// </summary>
public sealed class MapEntryOperator
{
    public required PhysicalOperator Key { get; init; }
    public required PhysicalOperator Value { get; init; }
}

/// <summary>
/// Array constructor: [a, b, c] or array { expr }
/// </summary>
public sealed class ArrayConstructorOperator : PhysicalOperator
{
    public required ArrayConstructorKind Kind { get; init; }
    public required IReadOnlyList<PhysicalOperator> Members { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var array = new List<object?>();
        if (Kind == ArrayConstructorKind.Square)
        {
            // Square: each member expression becomes a single array entry (may be a sequence)
            foreach (var member in Members)
            {
                var items = new List<object?>();
                await foreach (var item in member.ExecuteAsync(context))
                    items.Add(item);
                array.Add(items.Count == 1 ? items[0] : items.Count == 0 ? null : items.ToArray());
            }
        }
        else
        {
            // Curly: enclosed expr sequence becomes the array
            foreach (var member in Members)
            {
                await foreach (var item in member.ExecuteAsync(context))
                    array.Add(item);
            }
        }
        // Return as List<object?> — NOT object?[] — so that VariableOperator
        // recognises this as an XDM array (single item) rather than an XDM
        // sequence (which would be decomposed into individual items).
        yield return array;
    }
}

/// <summary>
/// Lookup expression: base?key or base?*
/// Per XQuery 3.1 spec: distributes lookup over each item in the base sequence.
/// Key can be a sequence — each key is looked up on each item.
/// </summary>
public sealed class LookupOperator : PhysicalOperator
{
    public required PhysicalOperator Base { get; init; }
    public PhysicalOperator? Key { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Collect all base items — postfix lookup distributes over the sequence
        var baseItems = new List<object?>();
        await foreach (var item in Base.ExecuteAsync(context))
            baseItems.Add(item);

        if (baseItems.Count == 0)
            yield break;

        if (Key == null)
        {
            // Wildcard lookup ?* — return all values from each item
            foreach (var baseVal in baseItems)
            {
                await foreach (var v in LookupHelper.WildcardLookup(baseVal))
                    yield return v;
            }
            yield break;
        }

        // Collect all keys — key can be a sequence like ?(1 to 3)
        var keys = new List<object?>();
        await foreach (var item in Key.ExecuteAsync(context))
            keys.Add(item);

        // Empty key sequence → empty result (not an error)
        if (keys.Count == 0)
            yield break;

        foreach (var baseVal in baseItems)
        {
            foreach (var rawKey in keys)
            {
                await foreach (var v in LookupHelper.LookupByKey(baseVal, rawKey))
                    yield return v;
            }
        }
    }
}

/// <summary>
/// Unary lookup operator (?key) — looks up a key on the context item.
/// Per XQuery 3.1 spec: key can be a sequence; each key is looked up.
/// Context item must be a map, array, or function — otherwise XPTY0004.
/// </summary>
public sealed class UnaryLookupOperator : PhysicalOperator
{
    public PhysicalOperator? Key { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var contextItem = context.ContextItem;
        if (contextItem == null)
            yield break;

        if (Key == null)
        {
            // Wildcard lookup ?* — return all values from context item
            await foreach (var v in LookupHelper.WildcardLookup(contextItem))
                yield return v;
            yield break;
        }

        // Collect all keys — key can be a sequence
        var keys = new List<object?>();
        await foreach (var item in Key.ExecuteAsync(context))
            keys.Add(item);

        // Empty key sequence → empty result
        if (keys.Count == 0)
            yield break;

        foreach (var rawKey in keys)
        {
            await foreach (var v in LookupHelper.LookupByKey(contextItem, rawKey))
                yield return v;
        }
    }
}

/// <summary>
/// Shared lookup logic for LookupOperator and UnaryLookupOperator.
/// </summary>
internal static class LookupHelper
{
    public static async IAsyncEnumerable<object?> WildcardLookup(object? item)
    {
        await Task.CompletedTask;
        if (item is Dictionary<object, object?> map)
        {
            foreach (var value in map.Values)
            {
                if (value is object?[] valSeq)
                    foreach (var v in valSeq)
                        yield return v;
                else if (value != null)
                    yield return value;
                // null represents () — yield nothing
            }
        }
        else if (item is IList<object?> arr)
        {
            foreach (var member in arr)
            {
                if (member is object?[] memberSeq)
                    foreach (var mv in memberSeq)
                        yield return mv;
                else if (member != null)
                    yield return member;
                // null represents () — yield nothing (empty sequence)
            }
        }
        else
        {
            throw new XQueryRuntimeException("XPTY0004",
                $"Lookup '?*' requires a map or array, got {item?.GetType().Name ?? "null"}");
        }
    }

    public static async IAsyncEnumerable<object?> LookupByKey(object? item, object? rawKey)
    {
        await Task.CompletedTask;
        // Atomize the key
        var keyVal = QueryExecutionContext.Atomize(rawKey);

        if (item is Dictionary<object, object?> m)
        {
            // For maps, try the key as-is first (preserves string keys from parse-json),
            // then fall back to integer-coerced key (for maps with integer keys like map{1:"a"}).
            object? val = null;
            bool found = keyVal != null && m.TryGetValue(keyVal, out val);
            if (!found && keyVal is string ks && long.TryParse(ks, out var kl))
                found = m.TryGetValue(kl, out val);
            if (found)
            {
                if (val is object?[] valSeq)
                    foreach (var v in valSeq)
                        yield return v;
                else if (val != null)
                    yield return val;
                // null represents () — yield nothing (empty sequence)
            }
        }
        else if (item is IList<object?> a)
        {
            // Arrays require xs:integer keys — decimal/double/float is XPTY0004
            if (keyVal is decimal || keyVal is double || keyVal is float)
                throw new XQueryRuntimeException("XPTY0004",
                    $"Array lookup requires an xs:integer key, got {keyVal?.GetType().Name} value {keyVal}");
            var index = Convert.ToInt32(keyVal) - 1; // XQuery arrays are 1-based
            if (index < 0 || index >= a.Count)
                throw new XQueryRuntimeException("FOAY0001",
                    $"Array index {index + 1} out of bounds (array size: {a.Count})");
            var member = a[index];
            if (member is object?[] memberSeq)
                foreach (var mv in memberSeq)
                    yield return mv;
            else if (member != null)
                yield return member;
            // null represents () — yield nothing (empty sequence)
        }
        else if (item is Delegate || item is PhoenixmlDb.XQuery.Ast.XQueryFunction)
        {
            // Functions can be called with lookup syntax — fn?(key) is fn(key)
            // For non-map/non-array functions, this is a type error
            throw new XQueryRuntimeException("XPTY0004",
                $"Lookup requires a map or array, got function");
        }
        else
        {
            throw new XQueryRuntimeException("XPTY0004",
                $"Lookup requires a map or array, got {item?.GetType().Name ?? "null"}");
        }
    }
}

/// <summary>
/// Named function reference: fn:name#arity
/// </summary>
public sealed class NamedFunctionRefOperator : PhysicalOperator
{
    public required QName Name { get; init; }
    public required int Arity { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        var func = context.Functions.Resolve(Name, Arity);
        if (func == null)
            throw new XQueryRuntimeException("XPST0017", $"Function {Name.LocalName}#{Arity} not found");

        // Per XPath 3.1 §3.1.6, a named function reference to a context-dependent function
        // captures the focus of its host expression at creation time. Wrap these so the
        // captured focus is restored on each invocation. This applies both to arity-0
        // functions (e.g., fn:name#0) and arity-1 functions that implicitly use the
        // context node (e.g., fn:lang#1, fn:id#1, fn:element-with-id#1).
        if (IsContextCaptureFunction(Name))
        {
            object? capturedItem;
            int capturedPos = 1, capturedSize = 1;
            try
            {
                capturedItem = context.ContextItem;
                capturedPos = context.Position;
                capturedSize = context.Last;
            }
            catch (XQueryRuntimeException) { capturedItem = QueryExecutionContext.AbsentFocus; }
            yield return new ContextBoundFunctionRef(func, capturedItem, context.StaticBaseUri, capturedPos, capturedSize);
            yield break;
        }

        // fn:function-lookup#2 and fn:function-lookup#1 must capture the static context
        // (static base URI, focus) at the point where the named reference is created,
        // so that a later invocation via the reference uses that captured context when
        // resolving the target function and constructing the returned context-dependent
        // function item. Without this, a reference escaping its defining module loses
        // the module's base URI.
        if ((Name.Namespace == FunctionNamespaces.Fn || Name.Prefix == "fn" || Name.Prefix == null)
            && Name.LocalName == "function-lookup")
        {
            object? capturedItem;
            int capturedPos = 1, capturedSize = 1;
            try
            {
                capturedItem = context.ContextItem;
                capturedPos = context.Position;
                capturedSize = context.Last;
            }
            catch (XQueryRuntimeException) { capturedItem = QueryExecutionContext.AbsentFocus; }
            yield return new ContextBoundFunctionRef(func, capturedItem, context.StaticBaseUri, capturedPos, capturedSize);
            yield break;
        }

        // A named function reference always has a fixed arity. Wrap variadic functions so
        // they expose the requested arity (and IsVariadic=false) regardless of whether the
        // requested arity equals the variadic minimum.
        if (func.IsVariadic)
            yield return new VariadicFunctionRefItem(func, Arity);
        else
            yield return func;
    }

    internal static bool IsContextCaptureFunction(QName name)
    {
        if (name.Namespace != FunctionNamespaces.Fn && name.Prefix != "fn" && name.Prefix != null)
            return false;
        return name.LocalName is "name" or "local-name" or "namespace-uri" or "node-name"
            or "string" or "data" or "number" or "normalize-space" or "string-length"
            or "base-uri" or "document-uri" or "nilled"
            or "root" or "path" or "generate-id" or "has-children" or "position" or "last"
            or "static-base-uri"
            // Arity-1 functions that use context node implicitly (e.g., fn:lang#1, fn:id#1, fn:element-with-id#1)
            or "lang" or "id" or "idref" or "element-with-id";
    }
}

/// <summary>
/// Function reference that captures a focus (context item) at creation time, as required
/// by XPath 3.1 §3.1.6 for context-dependent named function references. On invocation the
/// captured focus is pushed before delegating to the inner function.
/// </summary>
public sealed class ContextBoundFunctionRef : XQueryFunction
{
    private readonly XQueryFunction _inner;
    private readonly object? _capturedContextItem;
    private readonly string? _capturedStaticBaseUri;
    private readonly bool _hasCapturedStaticBaseUri;
    private readonly int _capturedPosition;
    private readonly int _capturedSize;

    public ContextBoundFunctionRef(XQueryFunction inner, object? capturedContextItem)
    {
        _inner = inner;
        _capturedContextItem = capturedContextItem;
        _hasCapturedStaticBaseUri = false;
        _capturedPosition = 1;
        _capturedSize = 1;
    }

    public ContextBoundFunctionRef(XQueryFunction inner, object? capturedContextItem, string? capturedStaticBaseUri)
    {
        _inner = inner;
        _capturedContextItem = capturedContextItem;
        _capturedStaticBaseUri = capturedStaticBaseUri;
        _hasCapturedStaticBaseUri = true;
        _capturedPosition = 1;
        _capturedSize = 1;
    }

    public ContextBoundFunctionRef(XQueryFunction inner, object? capturedContextItem, string? capturedStaticBaseUri, int position, int size)
    {
        _inner = inner;
        _capturedContextItem = capturedContextItem;
        _capturedStaticBaseUri = capturedStaticBaseUri;
        _hasCapturedStaticBaseUri = true;
        _capturedPosition = position;
        _capturedSize = size;
    }

    public override QName Name => _inner.Name;
    public override XdmSequenceType ReturnType => _inner.ReturnType;
    public override IReadOnlyList<FunctionParameterDef> Parameters => _inner.Parameters;
    public override bool IsVariadic => _inner.IsVariadic;
    public override int MaxArity => _inner.MaxArity;

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        PhoenixmlDb.XQuery.Ast.ExecutionContext context)
    {
        if (context is QueryExecutionContext qec)
        {
            qec.PushContextItem(_capturedContextItem, _capturedPosition, _capturedSize);
            string? savedBaseUri = qec.StaticBaseUri;
            if (_hasCapturedStaticBaseUri)
                qec.StaticBaseUri = _capturedStaticBaseUri;
            try
            {
                return await _inner.InvokeAsync(arguments, context);
            }
            finally
            {
                if (_hasCapturedStaticBaseUri)
                    qec.StaticBaseUri = savedBaseUri;
                qec.PopContextItem();
            }
        }
        return await _inner.InvokeAsync(arguments, context);
    }
}

/// <summary>
/// Wrapper for variadic function references that reports the requested arity.
/// E.g., concat#5 should report arity 5, not concat's minimum arity of 2.
/// </summary>
public sealed class VariadicFunctionRefItem : XQueryFunction
{
    private readonly XQueryFunction _inner;
    private readonly int _requestedArity;

    public VariadicFunctionRefItem(XQueryFunction inner, int requestedArity)
    {
        _inner = inner;
        _requestedArity = requestedArity;
    }

    public override QName Name => _inner.Name;
    public override XdmSequenceType ReturnType => _inner.ReturnType;
    public override IReadOnlyList<FunctionParameterDef> Parameters
    {
        get
        {
            // Generate parameter defs for the requested arity
            var baseParms = _inner.Parameters;
            if (_requestedArity <= baseParms.Count) return baseParms;
            var parms = new List<FunctionParameterDef>(baseParms);
            for (int i = baseParms.Count; i < _requestedArity; i++)
                parms.Add(new FunctionParameterDef
                {
                    Name = new QName(NamespaceId.None, $"arg{i + 1}"),
                    Type = XdmSequenceType.ZeroOrMoreItems
                });
            return parms;
        }
    }

    // A named function reference concat#N has fixed arity N — it's no longer variadic.
    public override bool IsVariadic => false;
    public override int MaxArity => _requestedArity;

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        PhoenixmlDb.XQuery.Ast.ExecutionContext context)
        => _inner.InvokeAsync(arguments, context);
}

/// <summary>
/// Inline function expression: function($x) { body }
/// </summary>
public sealed class InlineFunctionOperator : PhysicalOperator
{
    public required IReadOnlyList<FunctionParameter> Parameters { get; init; }
    public required XQueryExpression Body { get; init; }
    public XdmSequenceType? DeclaredReturnType { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        // Create a closure that captures the current scope
        yield return new InlineFunctionItem(Parameters, Body, context, DeclaredReturnType);
    }
}

/// <summary>
/// Represents a runtime inline function value (closure).
/// </summary>
public sealed class InlineFunctionItem : XQueryFunction
{
    private readonly IReadOnlyList<FunctionParameter> _parameters;
    private readonly XQueryExpression _body;
    private readonly QueryExecutionContext _capturedContext;
    private readonly Dictionary<QName, object?>? _closureVariables;
    private readonly XdmSequenceType? _declaredReturnType;
    private readonly string? _capturedBaseUri;
    private ExecutionPlan? _cachedPlan;

    public InlineFunctionItem(
        IReadOnlyList<FunctionParameter> parameters,
        XQueryExpression body,
        QueryExecutionContext context,
        XdmSequenceType? declaredReturnType = null,
        string? moduleBaseUri = null)
    {
        _parameters = parameters;
        _body = body;
        _capturedContext = context;
        _declaredReturnType = declaredReturnType;
        // Capture a snapshot of all in-scope variables to support closures.
        // Without this, variables from enclosing scopes (e.g., XSLT function params)
        // would be lost when the closure is invoked after the enclosing scope exits.
        _closureVariables = context.CaptureVariables();
        // Static base URI override: library-module functions carry their module's base-uri;
        // anonymous inline functions capture the current static base uri so closures preserve
        // the static context at creation time (XPath 3.1 §3.1.7).
        _capturedBaseUri = moduleBaseUri ?? context.StaticBaseUri;
    }

    public override QName Name => new(default, "anonymous");
    public override bool IsAnonymous => true;
    public override XdmSequenceType ReturnType => _declaredReturnType ?? XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        _parameters.Select(p => new FunctionParameterDef
        {
            Name = p.Name,
            Type = p.Type ?? XdmSequenceType.ZeroOrMoreItems
        }).ToList();

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var execContext = context as QueryExecutionContext ?? _capturedContext;

        execContext.EnterFunctionCall();
        // Per XPath/XQuery spec §3.1.5.1, the focus inside a function body is initially
        // undefined — accessing ., position(), or last() must raise XPDY0002 unless the
        // function explicitly sets a focus.
        execContext.PushContextItem(QueryExecutionContext.AbsentFocus);
        // Override the dynamic StaticBaseUri with the base URI captured at function-item
        // creation time (the module's declared base-uri, or the enclosing static context).
        var savedBaseUri = execContext.StaticBaseUri;
        bool baseUriOverridden = false;
        if (_capturedBaseUri != null)
        {
            execContext.StaticBaseUri = _capturedBaseUri;
            baseUriOverridden = true;
        }
        // Push closure scope with captured variables from enclosing context
        execContext.PushScope();
        if (_closureVariables != null)
        {
            foreach (var (name, value) in _closureVariables)
                execContext.BindVariable(name, value);
        }
        // Push parameter scope on top so params shadow closure variables
        execContext.PushScope();
        try
        {
            for (int i = 0; i < _parameters.Count && i < arguments.Count; i++)
            {
                var arg = arguments[i];
                var paramType = _parameters[i].Type;
                // Function argument coercion per XQuery 3.1 §3.1.5.2:
                // 1. Atomize node arguments
                // 2. Cast xs:untypedAtomic to expected type
                // 3. Numeric promotion (integer→double, etc.)
                // Unwrap single-item sequences (object?[])
                if (arg is object?[] singleArr && singleArr.Length == 1)
                    arg = singleArr[0];
                // Unwrap single-element arrays (List<object?>) ONLY when param expects atomic types,
                // NOT when param is item()/function(*)/array(*)/node-types (arrays are items).
                else if (arg is List<object?> singleList && singleList.Count == 1
                    && paramType?.ItemType is not (null or Ast.ItemType.Item or Ast.ItemType.Array
                        or Ast.ItemType.Function or Ast.ItemType.Map
                        or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                        or Ast.ItemType.ProcessingInstruction))
                    arg = singleList[0];

                // Handle multi-item sequences: object?[] is always a sequence;
                // List<object?> is an XDM array but treated as a sequence when param expects atomic types
                bool isMultiItemSequence = paramType != null
                    && (arg is object?[] seqArr && seqArr.Length != 1
                        || (arg is List<object?> seqList && seqList.Count != 1
                            && paramType.ItemType is not (Ast.ItemType.Item or Ast.ItemType.Array
                                or Ast.ItemType.Function or Ast.ItemType.Map
                                or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                                or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                                or Ast.ItemType.ProcessingInstruction)));
                if (isMultiItemSequence)
                {
                    var items = arg is object?[] sa ? sa : ((List<object?>)arg!).ToArray();
                    var isAtomicTgt = paramType!.ItemType is not (
                        Ast.ItemType.Item or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                        or Ast.ItemType.ProcessingInstruction or Ast.ItemType.Function
                        or Ast.ItemType.Map or Ast.ItemType.Array);
                    if (!isAtomicTgt)
                    {
                        // Non-atomic target: pass sequence as-is
                        execContext.BindVariable(_parameters[i].Name, arg);
                        continue;
                    }
                    // Atomic target with sequence occurrence (*,+): coerce each item
                    if (paramType.Occurrence is Ast.Occurrence.ZeroOrMore or Ast.Occurrence.OneOrMore)
                    {
                        var coerced = new object?[items.Length];
                        for (int j = 0; j < items.Length; j++)
                        {
                            var it = items[j];
                            if (it is XdmNode) it = QueryExecutionContext.AtomizeTyped(it);
                            if (it is XsUntypedAtomic ua)
                            {
                                try { it = TypeCastHelper.CastValue(ua.Value, paramType.ItemType); }
                                catch { /* keep original */ }
                            }
                            // Numeric promotion
                            if (it != null && !TypeCastHelper.MatchesItemType(it, paramType.ItemType)
                                && IsInlineParamPromotion(it, paramType.ItemType))
                            {
                                it = TypeCastHelper.PromoteNumeric(it, paramType.ItemType);
                            }
                            coerced[j] = it;
                        }
                        execContext.BindVariable(_parameters[i].Name, coerced);
                        continue;
                    }
                    // Sequence but single-item parameter — type error
                    throw new XQueryRuntimeException("XPTY0004",
                        $"Inline function parameter ${_parameters[i].Name.LocalName} expects " +
                        $"{paramType.ItemType} but got a sequence of {items.Length} items");
                }
                // Cardinality check: empty sequence (null) for exactly-one / one-or-more parameter
                if (arg == null && paramType != null
                    && paramType.Occurrence is Ast.Occurrence.ExactlyOne or Ast.Occurrence.OneOrMore)
                {
                    throw new XQueryRuntimeException("XPTY0004",
                        $"Parameter ${_parameters[i].Name.LocalName} expects {paramType} " +
                        $"but got an empty sequence");
                }
                if (paramType != null && arg != null
                    && paramType.ItemType != Ast.ItemType.Item
                    && paramType.ItemType != Ast.ItemType.AnyAtomicType)
                {
                    var coercedArg = arg;
                    // Only atomize for atomic parameter types (not node types like element(), document-node())
                    var isAtomicParamType = paramType.ItemType is not (
                        Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                        or Ast.ItemType.ProcessingInstruction
                        or Ast.ItemType.Function or Ast.ItemType.Map or Ast.ItemType.Array);
                    if (coercedArg is XdmNode && isAtomicParamType)
                        coercedArg = QueryExecutionContext.AtomizeTyped(coercedArg);
                    // XPath/XQuery 3.0+ §3.1.5.1 function coercion: implicit cast from
                    // xs:untypedAtomic to a namespace-sensitive type (xs:QName, xs:NOTATION)
                    // during function-argument coercion is NOT allowed — raise XPTY0117.
                    // (Explicit "cast as xs:QName" inside a cast expression IS allowed per bug 16089.)
                    if (coercedArg is XsUntypedAtomic && paramType.ItemType == Ast.ItemType.QName)
                    {
                        throw new XQueryRuntimeException("XPTY0117",
                            $"Implicit cast from xs:untypedAtomic to xs:QName is not allowed " +
                            $"during function coercion (parameter ${_parameters[i].Name.LocalName})");
                    }
                    // Cast untypedAtomic to expected type
                    if (coercedArg is XsUntypedAtomic ua)
                    {
                        try { coercedArg = TypeCastHelper.CastValue(ua.Value, paramType.ItemType); }
                        catch { /* keep original if cast fails */ }
                    }
                    // Numeric/URI promotion: convert value to target type
                    if (coercedArg != null
                        && coercedArg is not XsUntypedAtomic
                        && !TypeCastHelper.MatchesItemType(coercedArg, paramType.ItemType)
                        && IsInlineParamPromotion(coercedArg, paramType.ItemType))
                    {
                        coercedArg = TypeCastHelper.PromoteNumeric(coercedArg, paramType.ItemType);
                    }
                    // Type check after coercion and promotion
                    if (coercedArg != null
                        && coercedArg is not XsUntypedAtomic
                        && !TypeCastHelper.MatchesItemType(coercedArg, paramType.ItemType))
                    {
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Inline function parameter ${_parameters[i].Name.LocalName} expects " +
                            $"{paramType.ItemType} but got {coercedArg.GetType().Name}");
                    }
                    // Parameterized map/array type checking: check key/value/member types
                    if (coercedArg != null && paramType.ItemType is Ast.ItemType.Function
                        && paramType.FunctionParameterTypes != null
                        && coercedArg is XQueryFunction fnArg)
                    {
                        // Function coercion per XPath 3.1 §3.1.5.2:
                        // If the function item doesn't exactly match the target typed function type,
                        // wrap it in a coerced function that presents the target type signature.
                        // Actual argument/return type checking is deferred to invocation time.
                        if (fnArg.Arity == paramType.FunctionParameterTypes.Count
                            && !TypeCastHelper.MatchesFunctionType(fnArg,
                                paramType.FunctionParameterTypes, paramType.FunctionReturnType))
                        {
                            coercedArg = new CoercedFunctionItem(fnArg,
                                paramType.FunctionParameterTypes, paramType.FunctionReturnType);
                        }
                    }
                    else if (coercedArg != null && paramType.ItemType is not Ast.ItemType.Function)
                    {
                        var items = TypeCastHelper.NormalizeToList(coercedArg);
                        if (!TypeCastHelper.MatchesType(items, paramType))
                        {
                            throw new XQueryRuntimeException("XPTY0004",
                                $"Inline function parameter ${_parameters[i].Name.LocalName} does not match " +
                                $"parameterized type {paramType.ItemType}");
                        }
                    }
                    arg = coercedArg;
                }
                execContext.BindVariable(_parameters[i].Name, arg);
            }

            // Cache the execution plan - optimize only once per function instance
            _cachedPlan ??= new Optimizer.QueryOptimizer()
                .Optimize(_body, new Optimizer.OptimizationContext { Container = default });

            var results = new List<object?>();
            await foreach (var item in _cachedPlan.Root.ExecuteAsync(execContext))
                results.Add(item);

            var result = results.Count switch
            {
                0 => (object?)null,
                1 => results[0],
                _ => results.ToArray()
            };

            // Return type checking per XQuery 3.1 §3.1.5.1:
            // If a return type is declared, coerce/check the result.
            if (_declaredReturnType != null
                && _declaredReturnType.ItemType != Ast.ItemType.Item
                && _declaredReturnType.ItemType != Ast.ItemType.AnyAtomicType)
            {
                result = CoerceReturnValue(result, _declaredReturnType);
            }

            return result;
        }
        finally
        {
            execContext.PopScope(); // parameter scope
            execContext.PopScope(); // closure scope
            execContext.PopContextItem(); // absent focus
            if (baseUriOverridden)
                execContext.StaticBaseUri = savedBaseUri;
            execContext.ExitFunctionCall();
        }
    }

    /// <summary>
    /// Wraps a function item with a coerced type signature per XPath 3.1 §3.1.5.2.
    /// The wrapper preserves the original function's name and identity but presents
    /// the target type signature. Actual argument/return type checking is deferred to
    /// invocation time.
    /// </summary>
    internal sealed class CoercedFunctionItem : XQueryFunction
    {
        private readonly XQueryFunction _inner;
        private readonly IReadOnlyList<FunctionParameterDef> _targetParams;
        private readonly XdmSequenceType _targetReturnType;

        public CoercedFunctionItem(
            XQueryFunction inner,
            IReadOnlyList<XdmSequenceType> targetParamTypes,
            XdmSequenceType? targetReturnType)
        {
            _inner = inner;
            _targetReturnType = targetReturnType ?? XdmSequenceType.ZeroOrMoreItems;
            // Build parameter defs with the target types but preserve parameter names from inner
            var paramDefs = new FunctionParameterDef[targetParamTypes.Count];
            for (int i = 0; i < targetParamTypes.Count; i++)
            {
                var name = i < inner.Parameters.Count
                    ? inner.Parameters[i].Name
                    : new QName(default, $"arg{i}");
                paramDefs[i] = new FunctionParameterDef
                {
                    Name = name,
                    Type = targetParamTypes[i]
                };
            }
            _targetParams = paramDefs;
        }

        public override QName Name => _inner.Name;
        public override bool IsAnonymous => _inner.IsAnonymous;
        public override XdmSequenceType ReturnType => _targetReturnType;
        public override IReadOnlyList<FunctionParameterDef> Parameters => _targetParams;

        public override async ValueTask<object?> InvokeAsync(
            IReadOnlyList<object?> arguments,
            Ast.ExecutionContext context)
        {
            // Coerce arguments to target types before delegating to inner function.
            // This ensures the inner function sees values in the target type's domain,
            // causing XPTY0004 when inner param type is narrower than target type.
            var coerced = new object?[arguments.Count];
            for (int i = 0; i < arguments.Count && i < _targetParams.Count; i++)
            {
                var arg = arguments[i];
                var targetType = _targetParams[i].Type;
                if (arg != null && targetType != null
                    && targetType.ItemType is not (Ast.ItemType.Item or Ast.ItemType.AnyAtomicType))
                {
                    // Numeric promotion to target type
                    if (!TypeCastHelper.MatchesItemType(arg, targetType.ItemType)
                        && IsInlineParamPromotion(arg, targetType.ItemType))
                    {
                        arg = TypeCastHelper.PromoteNumeric(arg, targetType.ItemType);
                    }
                }
                coerced[i] = arg;
            }
            for (int i = _targetParams.Count; i < arguments.Count; i++)
                coerced[i] = arguments[i];
            return await _inner.InvokeAsync(coerced, context);
        }
    }

    /// <summary>
    /// Coerce a function return value to the declared return type.
    /// Applies numeric promotion and type checking per XQuery 3.1 §3.1.5.
    /// </summary>
    private static object? CoerceReturnValue(object? value, XdmSequenceType declaredType)
    {
        if (value == null)
        {
            if (declaredType.Occurrence is Ast.Occurrence.ExactlyOne or Ast.Occurrence.OneOrMore)
                throw new XQueryRuntimeException("XPTY0004",
                    $"Inline function declared return type {declaredType} but got empty sequence");
            return null;
        }

        // For sequence results, coerce each item
        if (value is object?[] arr)
        {
            var coerced = new object?[arr.Length];
            for (int i = 0; i < arr.Length; i++)
                coerced[i] = CoerceSingleReturnItem(arr[i], declaredType.ItemType);
            return coerced;
        }

        return CoerceSingleReturnItem(value, declaredType.ItemType);
    }

    private static object? CoerceSingleReturnItem(object? item, Ast.ItemType targetType)
    {
        if (item == null) return null;

        // Already matches target type — no coercion needed
        if (TypeCastHelper.MatchesItemType(item, targetType))
            return item;

        // Try numeric promotion (integer→double, integer→float, etc.)
        if (IsInlineParamPromotion(item, targetType))
            return TypeCastHelper.PromoteNumeric(item, targetType);

        // XQuery 3.1 §3.1.5.2: xs:untypedAtomic → cast to expected atomic type
        if (item is Xdm.XsUntypedAtomic ua)
        {
            try
            {
                return TypeCastHelper.CastValue(ua.Value, targetType);
            }
            catch
            {
                throw new XQueryRuntimeException("XPTY0004",
                    $"Cannot cast xs:untypedAtomic value '{ua.Value}' to {targetType}");
            }
        }

        // Type mismatch — raise XPTY0004
        throw new XQueryRuntimeException("XPTY0004",
            $"Inline function declared return type {targetType} but got {item.GetType().Name} value '{item}'");
    }

    private static bool IsInlineParamPromotion(object value, Ast.ItemType target)
    {
        // XQuery 3.1 §3.1.5.3: numeric type promotion + URI-to-string promotion
        return target switch
        {
            Ast.ItemType.Double => value is int or long or float or decimal or System.Numerics.BigInteger,
            Ast.ItemType.Float => value is int or long or decimal or System.Numerics.BigInteger,
            Ast.ItemType.Decimal => value is int or long or System.Numerics.BigInteger,
            Ast.ItemType.String => value is Xdm.XsAnyUri,
            _ => false
        };
    }
}

/// <summary>
/// Partial application of a named function: concat(?, 'x') → function($a) { concat($a, 'x') }
/// </summary>
public sealed class PartialApplicationOperator : PhysicalOperator
{
    public required XQueryFunction? ResolvedFunc { get; init; }
    public required QName FuncName { get; init; }
    /// <summary>Null entries are placeholders (?), non-null are fixed arguments with their operators.</summary>
    public required IReadOnlyList<(int index, PhysicalOperator op)?> ArgumentSlots { get; init; }
    public required int TotalArity { get; init; }
    public required int PlaceholderCount { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate fixed arguments eagerly, materializing the full sequence for each slot.
        var fixedValues = new object?[TotalArity];
        var isPlaceholder = new bool[TotalArity];
        for (int i = 0; i < ArgumentSlots.Count; i++)
        {
            if (ArgumentSlots[i] is { } slot)
            {
                object? singleItem = null;
                List<object?>? multiItems = null;
                var count = 0;
                await foreach (var item in slot.op.ExecuteAsync(context))
                {
                    count++;
                    if (count == 1) singleItem = item;
                    else if (count == 2) multiItems = [singleItem, item];
                    else multiItems!.Add(item);
                    if (count % 65536 == 0) context.CheckMaterializationLimit(count);
                }
                fixedValues[i] = count switch
                {
                    0 => null,
                    1 => singleItem,
                    _ => multiItems!.ToArray()
                };
            }
            else
            {
                isPlaceholder[i] = true;
            }
        }

        // Resolve the target function if not resolved at compile time.
        // If resolved to a DeclaredFunctionPlaceholder, re-resolve at runtime so we get
        // the actual DeclaredFunction registered by FunctionDeclarationOperator.
        // Always re-resolve at runtime so user-declared functions (which replace their
        // DeclaredFunctionPlaceholder entries at execution time) pick up the real implementation.
        var func = context.Functions.Resolve(FuncName, TotalArity)
                   ?? (ResolvedFunc is { } rf ? context.Functions.Resolve(rf.Name, TotalArity) : null)
                   ?? ResolvedFunc;
        if (func == null)
            throw new XQueryRuntimeException("XPST0017",
                $"Cannot partially apply: function {FuncName.LocalName}#{TotalArity} not found");

        // Note: Type checking of fixed arguments happens at invocation time, not at
        // partial application time. The spec creates a new function that supplies the
        // fixed arguments when called, so numeric promotion etc. applies at call time.

        yield return new PartiallyAppliedItem(func, fixedValues, isPlaceholder, PlaceholderCount);
    }
}

/// <summary>
/// Partial application of a dynamic function expression: $f(?, $p) → function($a) { $f($a, $p) }
/// </summary>
public sealed class DynamicPartialApplicationOperator : PhysicalOperator
{
    public required PhysicalOperator FuncExpression { get; init; }
    public required IReadOnlyList<(int index, PhysicalOperator op)?> ArgumentSlots { get; init; }
    public required int TotalArity { get; init; }
    public required int PlaceholderCount { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate the function expression
        object? funcVal = null;
        await foreach (var item in FuncExpression.ExecuteAsync(context))
        { funcVal = item; break; }

        if (funcVal is not XQueryFunction func)
            throw new XQueryRuntimeException("XPTY0004", "Value is not a function for partial application");

        // Arity must match: a function reference of arity N partially applied with M args
        // requires N == M (some of which are placeholders).
        if (!func.IsVariadic && TotalArity != func.Arity)
            throw new XQueryRuntimeException("XPTY0004",
                $"Function {func.Name.LocalName}() expects {func.Arity} argument(s), but {TotalArity} were supplied");

        // Evaluate fixed arguments eagerly, materializing the full sequence for each slot.
        var fixedValues = new object?[TotalArity];
        var isPlaceholder = new bool[TotalArity];
        for (int i = 0; i < ArgumentSlots.Count; i++)
        {
            if (ArgumentSlots[i] is { } slot)
            {
                object? singleItem = null;
                List<object?>? multiItems = null;
                var count = 0;
                await foreach (var item in slot.op.ExecuteAsync(context))
                {
                    count++;
                    if (count == 1) singleItem = item;
                    else if (count == 2) multiItems = [singleItem, item];
                    else multiItems!.Add(item);
                    if (count % 65536 == 0) context.CheckMaterializationLimit(count);
                }
                fixedValues[i] = count switch
                {
                    0 => null,
                    1 => singleItem,
                    _ => multiItems!.ToArray()
                };
            }
            else
            {
                isPlaceholder[i] = true;
            }
        }

        yield return new PartiallyAppliedItem(func, fixedValues, isPlaceholder, PlaceholderCount);
    }
}

/// <summary>
/// A partially applied function item. When called, merges placeholder and fixed arguments.
/// </summary>
public sealed class PartiallyAppliedItem : XQueryFunction
{
    private readonly XQueryFunction _targetFunc;
    private readonly object?[] _fixedValues;
    private readonly bool[] _isPlaceholder;
    private readonly int _placeholderCount;

    public PartiallyAppliedItem(XQueryFunction targetFunc, object?[] fixedValues, bool[] isPlaceholder, int placeholderCount)
    {
        _targetFunc = targetFunc;
        _fixedValues = fixedValues;
        _isPlaceholder = isPlaceholder;
        _placeholderCount = placeholderCount;
    }

    public override QName Name => _targetFunc.Name;
    public override bool IsAnonymous => true;
    public override XdmSequenceType ReturnType => _targetFunc.ReturnType;
    public override IReadOnlyList<FunctionParameterDef> Parameters
    {
        get
        {
            var result = new List<FunctionParameterDef>();
            var sourceParams = _targetFunc.Parameters;
            for (int i = 0; i < _isPlaceholder.Length; i++)
            {
                if (_isPlaceholder[i])
                {
                    result.Add(i < sourceParams.Count
                        ? sourceParams[i]
                        : new FunctionParameterDef { Name = new QName(default, $"arg{i}"), Type = XdmSequenceType.ZeroOrMoreItems });
                }
            }
            return result;
        }
    }

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Merge fixed values and placeholder arguments
        var mergedArgs = new object?[_fixedValues.Length];
        int placeholderIdx = 0;
        for (int i = 0; i < mergedArgs.Length; i++)
        {
            if (_isPlaceholder[i])
            {
                mergedArgs[i] = placeholderIdx < arguments.Count ? arguments[placeholderIdx] : null;
                placeholderIdx++;
            }
            else
            {
                mergedArgs[i] = _fixedValues[i];
            }
        }
        // If the target was a placeholder for a user-declared function (captured at compile/plan time),
        // resolve to the real DeclaredFunction at invoke time.
        var target = _targetFunc;
        if (target.GetType().Name == "DeclaredFunctionPlaceholder" && context is QueryExecutionContext qec)
            target = qec.Functions.Resolve(target.Name, mergedArgs.Length) ?? target;

        // Validate argument types against declared parameter types (XPath 3.0 §3.1.5.1)
        var targetParams = target.Parameters;
        for (int i = 0; i < mergedArgs.Length && i < targetParams.Count; i++)
        {
            if (targetParams[i].Type != null)
                TypeCastHelper.ValidateDynamicFunctionArg(mergedArgs[i], targetParams[i].Type, target.Name.LocalName, i);
        }

        return await target.InvokeAsync(mergedArgs, context);
    }
}

/// <summary>
/// Dynamic function call: $f(args)
/// </summary>
public sealed class DynamicFunctionCallOperator : PhysicalOperator
{
    public required PhysicalOperator FunctionExpression { get; init; }
    public required IReadOnlyList<PhysicalOperator> Arguments { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? funcVal = null;
        int funcItemCount = 0;
        await foreach (var item in FunctionExpression.ExecuteAsync(context))
        {
            funcItemCount++;
            if (funcItemCount == 1) funcVal = item;
            else
                throw new XQueryRuntimeException("XPTY0004",
                    "Dynamic function call requires a single function item, got a sequence");
        }
        if (funcItemCount == 0)
            throw new XQueryRuntimeException("XPTY0004",
                "Dynamic function call requires a single function item, got empty sequence");

        // XPath 3.0: Maps are callable as functions: $map($key) ≡ map:get($map, $key)
        if (funcVal is Dictionary<object, object?> map)
        {
            if (Arguments.Count != 1)
                throw new XQueryRuntimeException("XPTY0004", $"A map used as a function requires exactly 1 argument, but {Arguments.Count} were supplied");
            object? key = null;
            int keyCount = 0;
            if (Arguments.Count > 0)
            {
                await foreach (var item in Arguments[0].ExecuteAsync(context))
                {
                    keyCount++;
                    if (keyCount == 1) key = QueryExecutionContext.AtomizeTyped(item);
                    else throw new XQueryRuntimeException("XPTY0004",
                        "A map used as a function requires a single key, got a sequence");
                }
            }
            if (keyCount == 0)
                throw new XQueryRuntimeException("XPTY0004",
                    "A map used as a function requires a single key, got empty sequence");
            if (key != null && Functions.MapKeyHelper.TryGetValue(map, key, out var mapVal))
            {
                // Map values can be sequences (stored as object?[])
                if (mapVal is object?[] mapArr)
                {
                    foreach (var mv in mapArr)
                        yield return mv;
                }
                else
                    yield return mapVal;
            }
            yield break;
        }

        // XPath 3.0: Arrays are callable as functions: $array($pos) ≡ array:get($array, $pos)
        if (funcVal is IList<object?> array)
        {
            if (Arguments.Count != 1)
                throw new XQueryRuntimeException("XPTY0004", $"An array used as a function requires exactly 1 argument, but {Arguments.Count} were supplied");
            object? key = null;
            if (Arguments.Count > 0)
            {
                await foreach (var item in Arguments[0].ExecuteAsync(context))
                {
                    key = context.AtomizeWithNodes(item);
                    break;
                }
            }
            if (key != null)
            {
                // Arrays require xs:integer keys — decimal/double/float is XPTY0004
                if (key is decimal || key is double || key is float)
                    throw new XQueryRuntimeException("XPTY0004",
                        $"Array function call requires an xs:integer argument, got {key.GetType().Name} value {key}");
                var position = Convert.ToInt32(key);
                if (position < 1 || position > array.Count)
                    throw new XQueryRuntimeException("FOAY0001",
                        $"Array index {position} out of bounds (array size: {array.Count})");
                var member = array[position - 1];
                // Array members that are sequences (object?[]) need unwrapping
                if (member is object?[] memberSeq)
                {
                    foreach (var mv in memberSeq)
                        yield return mv;
                }
                else
                    yield return member;
            }
            yield break;
        }

        if (funcVal is not XQueryFunction func)
            throw new XQueryRuntimeException("XPTY0004", "Value is not a function");

        // XSLT 3.0: Some functions (current-group, current-grouping-key, current) cannot be
        // called dynamically through function references — raise the appropriate error
        if (func.DynamicCallErrorCode is { } errorCode)
            throw new XQueryRuntimeException(errorCode,
                $"Function {func.Name.LocalName}() cannot be called dynamically via a function reference");

        // Check arity: number of arguments must match function's expected arity
        if (!func.IsVariadic && Arguments.Count != func.Arity)
            throw new XQueryRuntimeException("XPTY0004",
                $"Function {func.Name.LocalName}() expects {func.Arity} argument(s), but {Arguments.Count} were supplied");

        // Evaluate arguments
        var args = new List<object?>();
        foreach (var argOp in Arguments)
        {
            var argValues = new List<object?>();
            await foreach (var item in argOp.ExecuteAsync(context))
                argValues.Add(item);
            args.Add(argValues.Count == 1 ? argValues[0] : argValues.ToArray());
        }

        // Validate argument types against declared parameter types (XPath 3.0 §3.1.5.1)
        var funcParams = func.Parameters;
        for (int i = 0; i < args.Count && i < funcParams.Count; i++)
        {
            if (funcParams[i].Type != null)
                TypeCastHelper.ValidateDynamicFunctionArg(args[i], funcParams[i].Type, func.Name.LocalName, i);
        }

        // Per XPath/XQuery spec §3.1.5.1: when the callee is a plain built-in that isn't a
        // ContextBoundFunctionRef (which already carries its captured focus), reset the focus
        // so that e.g. `let $f := name#0 return <a/>/$f()` raises XPDY0002 — the function
        // does not inherit the caller's focus. Inline functions handle this inside InvokeAsync.
        object? result;
        var resetFocus = func is not ContextBoundFunctionRef and not InlineFunctionItem;
        if (resetFocus)
            context.PushContextItem(QueryExecutionContext.AbsentFocus);
        try
        {
            result = await func.InvokeAsync(args, context);
        }
        finally
        {
            if (resetFocus)
                context.PopContextItem();
        }

        // XDM arrays and maps are items, not sequences — yield as-is based on return type
        if (result is IDictionary<object, object?>
            || (result is List<object?> && func.ReturnType?.ItemType is Ast.ItemType.Array or Ast.ItemType.Map))
        {
            yield return result;
        }
        else if (result is IEnumerable<object?> seq && result is not string)
        {
            foreach (var item in seq)
                yield return item;
        }
        else if (result != null)
        {
            yield return result;
        }
    }
}

/// <summary>
/// Module operator: executes declarations then body.
/// </summary>
public sealed class ModuleOperator : PhysicalOperator
{
    public required IReadOnlyList<PhysicalOperator> Declarations { get; init; }
    public required PhysicalOperator Body { get; init; }
    public Dictionary<string, string>? NamespaceBindings { get; init; }
    public Dictionary<string, Analysis.DecimalFormatProperties>? DecimalFormats { get; init; }
    public string? DefaultCollation { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Set namespace bindings from prolog for runtime use by computed constructors
        if (NamespaceBindings != null)
        {
            context.PrefixNamespaceBindings = NamespaceBindings;
            // Save a snapshot of the prolog bindings so ElementConstructorOperator can
            // distinguish prolog-level bindings from those added by enclosing constructors.
            context.PrologNamespaceBindings = new Dictionary<string, string>(NamespaceBindings);
        }

        // Set default collation from prolog declaration
        if (DefaultCollation != null)
            context.DefaultCollation = DefaultCollation;

        // Set decimal-format properties from prolog for format-number()
        if (DecimalFormats != null)
        {
            foreach (var (name, props) in DecimalFormats)
                context.DecimalFormats[name] = props;
        }

        // Register function declarations first (they don't depend on variable values at registration time)
        foreach (var decl in Declarations.Where(d => d is FunctionDeclarationOperator))
        {
            await foreach (var _ in decl.ExecuteAsync(context))
            {
            }
        }

        // Collect variable and context item declarations for lazy evaluation.
        // XQuery 3.1 §2.1.1: "All variable declarations [...] are visible throughout the module"
        // Forward references between variables require lazy/on-demand initialization.
        var pendingVarDecls = new Dictionary<QName, VariableDeclarationOperator>(QNameComparer.Instance);
        var otherDecls = new List<PhysicalOperator>();
        ContextItemDeclarationOperator? contextItemDecl = null;

        foreach (var decl in Declarations)
        {
            if (decl is VariableDeclarationOperator varDecl)
                pendingVarDecls[varDecl.VariableName] = varDecl;
            else if (decl is ContextItemDeclarationOperator ctxDecl)
                contextItemDecl = ctxDecl;
            else if (decl is not FunctionDeclarationOperator)
                otherDecls.Add(decl);
        }

        // Set of variables currently being initialized (cycle detection)
        var initializing = new HashSet<QName>(QNameComparer.Instance);
        var initialized = new HashSet<QName>(QNameComparer.Instance);

        // Lazy initializer: when a variable is referenced before it's been evaluated,
        // evaluate it on demand (supporting forward references).
        var previousFallback = context.VariableFallback;
        context.VariableFallback = (name) =>
        {
            if (pendingVarDecls.TryGetValue(name, out var pending) && !initialized.Contains(name))
            {
                if (initializing.Contains(name))
                    throw new XQueryRuntimeException("XQDY0054",
                        $"Circular dependency detected initializing variable ${name}");

                InitializeVariableSync(pending, context, initializing, initialized);
                // After initialization, the variable should be bound
                try
                {
                    var val = context.GetVariable(name);
                    return (true, val);
                }
                catch
                {
                    return (false, null);
                }
            }
            return previousFallback?.Invoke(name) ?? (false, null);
        };

        // Initialize context item first if possible — but if its initializer references
        // a variable, the lazy fallback will handle the forward reference.
        if (contextItemDecl != null)
        {
            await foreach (var _ in contextItemDecl.ExecuteAsync(context))
            {
            }
        }

        // Evaluate all variable declarations (lazy fallback handles forward references)
        foreach (var (varName, varDecl) in pendingVarDecls)
        {
            if (!initialized.Contains(varName))
            {
                await InitializeVariableAsync(varDecl, context, initializing, initialized);
            }
        }

        // Restore previous fallback
        context.VariableFallback = previousFallback;

        // Process remaining non-variable, non-function, non-context-item declarations
        foreach (var decl in otherDecls)
        {
            await foreach (var _ in decl.ExecuteAsync(context))
            {
            }
        }

        // Execute the body
        await foreach (var item in Body.ExecuteAsync(context))
            yield return item;
    }

    private static async Task InitializeVariableAsync(
        VariableDeclarationOperator varDecl,
        QueryExecutionContext context,
        HashSet<QName> initializing,
        HashSet<QName> initialized)
    {
        initializing.Add(varDecl.VariableName);
        await foreach (var _ in varDecl.ExecuteAsync(context))
        {
        }
        initializing.Remove(varDecl.VariableName);
        initialized.Add(varDecl.VariableName);
    }

    private static void InitializeVariableSync(
        VariableDeclarationOperator varDecl,
        QueryExecutionContext context,
        HashSet<QName> initializing,
        HashSet<QName> initialized)
    {
        initializing.Add(varDecl.VariableName);
        var enumerator = varDecl.ExecuteAsync(context).GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        initializing.Remove(varDecl.VariableName);
        initialized.Add(varDecl.VariableName);
    }

    /// <summary>
    /// QName equality comparer that matches by namespace + local name (or prefix + local name if no namespace).
    /// </summary>
    private sealed class QNameComparer : IEqualityComparer<QName>
    {
        public static readonly QNameComparer Instance = new();

        public bool Equals(QName x, QName y)
        {
            if (x.LocalName != y.LocalName) return false;
            // Compare by namespace if available, otherwise by prefix
            var xNs = x.ExpandedNamespace;
            var yNs = y.ExpandedNamespace;
            if (xNs != null || yNs != null)
                return xNs == yNs;
            return x.Prefix == y.Prefix;
        }

        public int GetHashCode(QName obj)
        {
            var ns = obj.ExpandedNamespace;
            return HashCode.Combine(obj.LocalName, ns ?? obj.Prefix ?? "");
        }
    }
}

/// <summary>
/// Sets the context item from a prolog declaration: declare context item := expr;
/// </summary>
public sealed class ContextItemDeclarationOperator : PhysicalOperator
{
    public PhysicalOperator? ValueOperator { get; init; }
    public XdmSequenceType? TypeConstraint { get; init; }
    public bool IsExternal { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // If external: use externally-supplied context item if available
        if (IsExternal)
        {
            // Check if there's already a context item pushed externally.
            // ContextItem returns null when the stack is empty (no focus set at all),
            // and throws XPDY0002 when AbsentFocus sentinel is on top.
            // Both cases mean no external context item was supplied.
            bool hasExternal = false;
            try
            {
                var existing = context.ContextItem;
                if (existing != null)
                {
                    hasExternal = true;
                    // External context item supplied — type check if constrained
                    if (TypeConstraint != null)
                        TypeCastHelper.RequireSequenceTypeMatch(existing, TypeConstraint, "declare context item");
                }
            }
            catch (XQueryRuntimeException ex) when (ex.ErrorCode == "XPDY0002")
            {
                // AbsentFocus sentinel — no external context
            }

            if (hasExternal)
                yield break;
            // Fall through to default value
        }

        if (ValueOperator == null)
        {
            // External with no default and no externally-supplied context
            if (IsExternal)
                throw new XQueryRuntimeException("XPDY0002", "The context item is absent");
            yield break;
        }

        // Evaluate the default value expression
        var items = new List<object?>();
        await foreach (var item in ValueOperator.ExecuteAsync(context))
            items.Add(item);

        // Context item must be exactly one item (XPTY0004 for empty or multi-item sequences)
        if (items.Count == 0)
            throw new XQueryRuntimeException("XPTY0004",
                "Context item declaration value is an empty sequence");
        if (items.Count > 1)
            throw new XQueryRuntimeException("XPTY0004",
                "Context item declaration value contains more than one item");

        var value = items[0];

        // Type check against declared type (no function conversion rules apply — XPTY0004)
        if (TypeConstraint != null)
            TypeCastHelper.RequireSequenceTypeMatch(value, TypeConstraint, "declare context item");

        context.PushContextItem(value);
        yield break;
    }
}

/// <summary>
/// Variable declaration operator: declare variable $name := expr;
/// </summary>
public sealed class VariableDeclarationOperator : PhysicalOperator
{
    public required QName VariableName { get; init; }
    public PhysicalOperator? ValueOperator { get; init; }
    public bool IsExternal { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // For external variables, check if a binding was provided before falling back to the default.
        if (IsExternal && context.TryGetExternalVariable(VariableName, out var externalValue))
        {
            // XQuery §3.10.3: type check external variable values against declared type
            if (TypeDeclaration != null)
                TypeCastHelper.RequireSequenceTypeMatch(externalValue, TypeDeclaration, $"declare variable ${VariableName}");
            context.BindVariable(VariableName, externalValue);
            yield break;
        }

        if (ValueOperator == null)
        {
            // External variable with no default and no binding
            throw new XQueryRuntimeException("XPST0008",
                $"External variable ${VariableName} was not bound and has no default value");
        }

        var values = new List<object?>();
        await foreach (var item in ValueOperator.ExecuteAsync(context))
            values.Add(item);

        object? value = values.Count switch
        {
            0 => null,
            1 => values[0],
            _ => values.ToArray()
        };

        // XQuery §3.10.3: type check variable value against declared type
        if (TypeDeclaration != null)
            TypeCastHelper.RequireSequenceTypeMatch(value, TypeDeclaration, $"declare variable ${VariableName}");

        context.BindVariable(VariableName, value);
        yield break;
    }
}

/// <summary>
/// Function declaration operator: declare function local:name($params) { body };
/// </summary>
public sealed class FunctionDeclarationOperator : PhysicalOperator
{
    public required QName FunctionName { get; init; }
    public required IReadOnlyList<FunctionParameter> Parameters { get; init; }
    public required XQueryExpression Body { get; init; }
    public XdmSequenceType? DeclaredReturnType { get; init; }
    /// <summary>
    /// Static base URI of the module in which this function was declared. When the
    /// function executes, this overrides the caller's static base URI.
    /// </summary>
    public string? ModuleBaseUri { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        var func = new InlineFunctionItem(Parameters, Body, context, moduleBaseUri: ModuleBaseUri);
        context.Functions.Register(new DeclaredFunction(FunctionName, Parameters, func, DeclaredReturnType, ModuleBaseUri));
        yield break;
    }
}

/// <summary>
/// Wraps an InlineFunctionItem as a registered function with the correct name/arity.
/// </summary>
internal sealed class DeclaredFunction : XQueryFunction
{
    private readonly QName _name;
    private readonly IReadOnlyList<FunctionParameter> _parameters;
    private readonly InlineFunctionItem _implementation;
    private readonly XdmSequenceType? _returnType;

    public DeclaredFunction(QName name, IReadOnlyList<FunctionParameter> parameters, InlineFunctionItem implementation, XdmSequenceType? returnType = null, string? moduleBaseUri = null)
    {
        _name = name;
        _parameters = parameters;
        _implementation = implementation;
        _returnType = returnType;
        _ = moduleBaseUri; // ModuleBaseUri is already propagated into the InlineFunctionItem at construction.
    }

    public override QName Name => _name;
    public override XdmSequenceType ReturnType => _returnType ?? XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        _parameters.Select(p => new FunctionParameterDef
        {
            Name = p.Name,
            Type = p.Type ?? XdmSequenceType.ZeroOrMoreItems
        }).ToList();

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var result = await _implementation.InvokeAsync(arguments, context);
        if (_returnType == null)
            return result;

        // Always check cardinality, even for item()/node() return types
        // XDM arrays (List<object?>) and maps (Dictionary) are single items, not sequences
        var resultItems = result switch
        {
            null => (IReadOnlyList<object?>)Array.Empty<object?>(),
            IDictionary<object, object?> => new object?[] { result },
            List<object?> => new object?[] { result },
            object?[] arr => arr,
            _ => new object?[] { result }
        };

        // Check occurrence (cardinality) for all return types
        if (_returnType.Occurrence == Occurrence.ExactlyOne && resultItems.Count != 1)
            throw new XQueryRuntimeException("XPTY0004",
                $"Result of function {_name.LocalName} does not match declared return type {_returnType}: expected exactly one item, got {resultItems.Count}");
        if (_returnType.Occurrence == Occurrence.OneOrMore && resultItems.Count == 0)
            throw new XQueryRuntimeException("XPTY0004",
                $"Result of function {_name.LocalName} does not match declared return type {_returnType}: expected one or more items, got empty sequence");
        if (_returnType.Occurrence == Occurrence.ZeroOrOne && resultItems.Count > 1)
            throw new XQueryRuntimeException("XPTY0004",
                $"Result of function {_name.LocalName} does not match declared return type {_returnType}: expected at most one item, got {resultItems.Count}");

        // For item() return type, only cardinality check is needed (already done above)
        if (_returnType.ItemType == ItemType.Item)
            return result;

        // For node-typed returns, enforce that result items match the kind test
        if (_returnType.ItemType is ItemType.Element or ItemType.Attribute
            or ItemType.Text or ItemType.Document or ItemType.Comment
            or ItemType.ProcessingInstruction or ItemType.Node)
        {
            foreach (var item in resultItems)
            {
                if (item != null)
                {
                    if (!TypeCastHelper.MatchesItemType(item, _returnType.ItemType))
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Result of function {_name.LocalName} does not match declared return type {_returnType}");
                    // Check element(name) / attribute(name) constraints
                    if (_returnType.ElementName != null && item is XdmElement el
                        && el.LocalName != _returnType.ElementName)
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Result of function {_name.LocalName} does not match declared return type element({_returnType.ElementName}): got element({el.LocalName})");
                    if (_returnType.AttributeName != null && item is XdmAttribute attr
                        && attr.LocalName != _returnType.AttributeName)
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Result of function {_name.LocalName} does not match declared return type attribute({_returnType.AttributeName}): got attribute({attr.LocalName})");
                }
            }
            return result;
        }

        // Only enforce coercion/promotion for atomic targets.
        var enforce = _returnType.ItemType is not (
            ItemType.Map or ItemType.Array or ItemType.Record);
        // For function-typed return, apply function coercion per XPath 3.1 §3.1.5.2:
        // wrap the function with the target type signature, deferring type checks to invocation time.
        if (_returnType.ItemType == ItemType.Function && _returnType.FunctionParameterTypes != null)
        {
            var anyCoerced = false;
            var coercedItems = new object?[resultItems.Count];
            for (int fi = 0; fi < resultItems.Count; fi++)
            {
                if (resultItems[fi] is XQueryFunction retFn
                    && retFn.Arity == _returnType.FunctionParameterTypes.Count
                    && !TypeCastHelper.MatchesFunctionType(retFn,
                        _returnType.FunctionParameterTypes, _returnType.FunctionReturnType))
                {
                    coercedItems[fi] = new InlineFunctionItem.CoercedFunctionItem(retFn,
                        _returnType.FunctionParameterTypes, _returnType.FunctionReturnType);
                    anyCoerced = true;
                }
                else
                {
                    coercedItems[fi] = resultItems[fi];
                }
            }
            if (anyCoerced)
                return coercedItems.Length == 1 ? coercedItems[0] : coercedItems;
            enforce = false;
        }
        else if (_returnType.ItemType == ItemType.Function)
        {
            enforce = false;
        }
        if (!enforce)
            return result;
        var qec = context as QueryExecutionContext;
        var items = resultItems;

        // Apply per-item function-conversion-rules coercion (atomize / cast / promote)
        var coerced = new object?[items.Count];
        var anyCoercion = false;
        var isAtomicTarget = _returnType.ItemType is not (
            ItemType.Node or ItemType.Element or ItemType.Attribute
            or ItemType.Text or ItemType.Document or ItemType.Comment
            or ItemType.ProcessingInstruction or ItemType.Function
            or ItemType.Map or ItemType.Array);
        for (int i = 0; i < items.Count; i++)
        {
            var v = items[i];
            if (v != null && isAtomicTarget)
            {
                if (v is XdmNode node)
                {
                    // Per XDM spec: comment and PI nodes have typed value xs:string,
                    // while text/element nodes have xs:untypedAtomic.
                    // Function conversion rules only cast xs:untypedAtomic to the target type,
                    // so comment/PI atomization must remain xs:string (not XsUntypedAtomic).
                    var isStringTypedNode = node is XdmComment or XdmProcessingInstruction;
                    var atomizedStr = qec != null
                        ? QueryExecutionContext.Atomize(v, qec.NodeProvider)
                        : QueryExecutionContext.Atomize(v, null);
                    v = (atomizedStr is string s && !isStringTypedNode) ? new XsUntypedAtomic(s) : atomizedStr;
                    anyCoercion = true;
                }
                if (v is XsUntypedAtomic ua)
                {
                    try { v = TypeCastHelper.CastValue(ua.Value?.Trim() ?? "", _returnType.ItemType); anyCoercion = true; }
                    catch { /* keep original */ }
                }
                else if (v != null
                    && !TypeCastHelper.MatchesItemType(v, _returnType.ItemType)
                    && IsReturnPromotion(v, _returnType.ItemType))
                {
                    v = PromoteReturn(v, _returnType.ItemType);
                    anyCoercion = true;
                }
            }
            coerced[i] = v;
        }

        if (!TypeCastHelper.MatchesType(coerced, _returnType))
        {
            throw new XQueryRuntimeException("XPTY0004",
                $"Result of function {_name.LocalName} does not match declared return type");
        }

        if (!anyCoercion) return result;
        return coerced.Length switch
        {
            0 => null,
            1 => coerced[0],
            _ => coerced
        };
    }

    private static bool IsReturnPromotion(object value, ItemType target) => target switch
    {
        ItemType.Double => value is int or long or float or decimal or BigInteger,
        ItemType.Float => value is int or long or decimal or BigInteger,
        ItemType.Decimal => value is int or long or BigInteger,
        ItemType.String => value is Xdm.XsAnyUri,
        _ => false
    };

    private static object? PromoteReturn(object value, ItemType target) => target switch
    {
        ItemType.Double => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
        ItemType.Float => Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture),
        ItemType.Decimal => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
        ItemType.String => value.ToString(),
        _ => value
    };
}

/// <summary>
/// Helper for type casting and type checking operations.
/// </summary>
public static class TypeCastHelper
{
    /// <summary>
    /// Wraps date/time/dateTime/duration parsing to convert .NET exceptions into FORG0001.
    /// </summary>
    internal static T SafeParseDateType<T>(Func<T> parse, string typeName, string input)
    {
        try
        {
            return parse();
        }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new XQueryRuntimeException("FORG0001",
                $"Cannot cast '{input}' to {typeName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Strict type check for let/for bindings: the value must either be xs:untypedAtomic
    /// (implicit conversion) or already match the target atomic type. Numeric-to-numeric
    /// subtype promotion is permitted per function-conversion rules.
    /// </summary>
    public static void RequireAtomicTypeMatch(object? value, ItemType targetType, string context)
    {
        if (value is null) return;
        if (value is Xdm.XsUntypedAtomic) return;
        bool ok = targetType switch
        {
            ItemType.String or ItemType.AnyAtomicType => value is string,
            ItemType.AnyUri => value is Xdm.XsAnyUri or string,
            ItemType.Integer => value is long or int or short or byte or System.Numerics.BigInteger,
            ItemType.Decimal => value is decimal or long or int or short or byte or System.Numerics.BigInteger,
            ItemType.Double => value is double or long or int or decimal or System.Numerics.BigInteger,
            ItemType.Float => value is float or double or long or int or decimal,
            ItemType.Boolean => value is bool,
            ItemType.Date => value is Xdm.XsDate,
            ItemType.DateTime => value is Xdm.XsDateTime,
            ItemType.Time => value is Xdm.XsTime,
            ItemType.QName => value is PhoenixmlDb.Core.QName,
            _ => true
        };
        if (!ok)
            throw new XQueryRuntimeException("XPTY0004",
                $"{context}: value of type {value.GetType().Name} does not match declared {targetType}");
    }

    /// <summary>
    /// Strict SequenceType matching for variable declarations (let, declare variable).
    /// XQuery §3.8.1, §3.10.3: NO promotion, NO untypedAtomic casting — only subtype matching.
    /// Raises XPTY0004 on mismatch.
    /// </summary>
    public static void RequireSequenceTypeMatch(object? value, XdmSequenceType declaredType, string context)
    {
        var items = value switch
        {
            null => Array.Empty<object?>(),
            object?[] arr => arr,
            _ => new[] { value }
        };

        if (!MatchesType(items, declaredType))
            throw new XQueryRuntimeException("XPTY0004",
                $"{context}: value does not match declared type {declaredType.ItemType}" +
                $"{declaredType.Occurrence switch { Occurrence.ZeroOrOne => "?", Occurrence.ZeroOrMore => "*", Occurrence.OneOrMore => "+", _ => "" }}");
    }

    /// <summary>
    /// Numeric type promotion: promotes a value to the target item type.
    /// Per XQuery 3.1 §3.1.5.3: xs:float/xs:double promotion from integer/decimal.
    /// Also handles xs:anyURI → xs:string promotion.
    /// </summary>
    public static object? PromoteNumeric(object? value, ItemType target)
    {
        if (value == null) return null;
        return target switch
        {
            ItemType.Double => Convert.ToDouble(value),
            ItemType.Float => Convert.ToSingle(value),
            ItemType.Decimal => Convert.ToDecimal(value),
            ItemType.String when value is Xdm.XsAnyUri uri => uri.Value,
            _ => value
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1508")]
    public static object? CastValue(object? value, ItemType targetType)
    {
        if (value == null)
            return null;

        // xs:error — the empty union type (XSD 1.1): no value can ever be cast to xs:error
        if (targetType == ItemType.Error)
            throw new XQueryRuntimeException("FORG0001",
                "Cannot cast to xs:error — xs:error has no values");

        // Node/function/map/array target types: return the value unchanged (don't atomize)
        if (targetType is ItemType.Node or ItemType.Element or ItemType.Attribute
            or ItemType.Text or ItemType.Document or ItemType.Comment
            or ItemType.ProcessingInstruction or ItemType.Item
            or ItemType.Map or ItemType.Array or ItemType.Function)
            return value;

        // Atomize XDM nodes before casting to atomic types
        value = QueryExecutionContext.Atomize(value);
        if (value == null)
            return null;

        // xs:untypedAtomic is treated as its string value for casting (XPath/XQuery spec §19.1)
        if (value is Xdm.XsUntypedAtomic untypedVal)
            value = untypedVal.Value;

        // XSD string subtypes: unwrap to plain string for casting purposes
        if (value is Xdm.XsTypedString typedStr)
            value = typedStr.Value;

        // Cross-type casting rejection per XQuery 3.1 casting table
        RejectInvalidCast(value, targetType);

        return targetType switch
        {
            ItemType.String => Functions.ConcatFunction.XQueryStringValue(value),
            ItemType.Integer => value switch
            {
                long l => l,
                int i => (long)i,
                BigInteger bi => bi >= long.MinValue && bi <= long.MaxValue ? (object)(long)bi : bi,
                bool b => b ? 1L : 0L,
                double d when d >= long.MinValue && d <= long.MaxValue => (long)d,
                double d => (BigInteger)d,
                decimal m when m >= long.MinValue && m <= long.MaxValue => (long)m,
                decimal m => (BigInteger)m,
                string s => long.TryParse(s, out var r) ? r
                    : BigInteger.TryParse(s, out var bi2) ? (object)bi2
                    : throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:integer"),
                _ => Convert.ToInt64(value)
            },
            ItemType.Double => value switch
            {
                double d => d,
                string s when s == "INF" || s == "+INF" => double.PositiveInfinity,
                string s when s == "-INF" => double.NegativeInfinity,
                string s when s == "NaN" => double.NaN,
                string s => double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) ? r
                    : throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:double"),
                bool b => b ? 1.0 : 0.0,
                BigInteger bi => (double)bi,
                _ => Convert.ToDouble(value)
            },
            ItemType.Float => value switch
            {
                float f => f,
                BigInteger bi => (float)bi,
                string s when s == "INF" || s == "+INF" => float.PositiveInfinity,
                string s when s == "-INF" => float.NegativeInfinity,
                string s when s == "NaN" => float.NaN,
                string s => float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) ? r
                    : throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:float"),
                _ => Convert.ToSingle(value)
            },
            ItemType.Decimal => value switch
            {
                decimal m => m,
                BigInteger bi => (decimal)bi,
                string s => decimal.TryParse(s, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) ? r
                    : throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:decimal"),
                _ => Convert.ToDecimal(value)
            },
            ItemType.Boolean => value switch
            {
                bool b => b,
                string s when s is "true" or "1" => true,
                string s when s is "false" or "0" => false,
                string s => throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:boolean"),
                long l => l != 0,
                int i => i != 0,
                double d => d != 0 && !double.IsNaN(d),
                decimal m => m != 0,
                _ => QueryExecutionContext.EffectiveBooleanValue(value)
            },
            ItemType.QName => value is PhoenixmlDb.Core.QName ? value : ParseQName(value?.ToString() ?? ""),
            ItemType.AnyUri => value is Xdm.XsAnyUri ? value : new Xdm.XsAnyUri(value?.ToString() ?? ""),
            ItemType.UntypedAtomic => value is Xdm.XsUntypedAtomic ? value : new Xdm.XsUntypedAtomic(Functions.ConcatFunction.XQueryStringValue(value)),
            ItemType.AnyAtomicType => value, // No conversion needed
            ItemType.Duration => value switch
            {
                Xdm.XsDuration d => d,
                TimeSpan ts => new Xdm.XsDuration(0, ts),
                YearMonthDuration ymd => new Xdm.XsDuration(ymd.TotalMonths, TimeSpan.Zero),
                string s => TypeCastHelper.SafeParseDateType(() => Xdm.XsDuration.Parse(s), "xs:duration", s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:duration")
            },
            ItemType.YearMonthDuration => value switch
            {
                YearMonthDuration ymd => ymd,
                Xdm.XsDuration dur => new YearMonthDuration(dur.TotalMonths),
                TimeSpan => new YearMonthDuration(0), // dayTimeDuration has 0 months component
                string s => YearMonthDuration.Parse(s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:yearMonthDuration")
            },
            ItemType.DayTimeDuration => value switch
            {
                TimeSpan ts => ts,
                Xdm.XsDuration dur => dur.DayTime,
                YearMonthDuration => TimeSpan.Zero, // yearMonthDuration has 0 days/time component
                string s => ParseDayTimeDuration(s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:dayTimeDuration")
            },
            ItemType.DateTime => value switch
            {
                Xdm.XsDateTime xdt => xdt,
                DateTimeOffset dto => new Xdm.XsDateTime(dto, true),
                Xdm.XsDate xd => new Xdm.XsDateTime(
                    new DateTimeOffset(xd.Date.ToDateTime(TimeOnly.MinValue), xd.Timezone ?? TimeSpan.Zero),
                    xd.Timezone.HasValue),
                string s => TypeCastHelper.SafeParseDateType(() => Xdm.XsDateTime.Parse(s), "xs:dateTime", s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:dateTime")
            },
            ItemType.Date => value switch
            {
                Xdm.XsDate xd => xd,
                Xdm.XsDateTime xdt => new Xdm.XsDate(DateOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null) { ExtendedYear = xdt.ExtendedYear },
                DateOnly d => new Xdm.XsDate(d, null),
                string s => TypeCastHelper.SafeParseDateType(() => Xdm.XsDate.Parse(s), "xs:date", s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:date")
            },
            ItemType.Time => value switch
            {
                Xdm.XsTime xt => xt,
                Xdm.XsDateTime xdt => new Xdm.XsTime(TimeOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null, xdt.FractionalTicks),
                TimeOnly t => new Xdm.XsTime(t, null, (int)(t.Ticks % TimeSpan.TicksPerSecond)),
                string s => TypeCastHelper.SafeParseDateType(() => Xdm.XsTime.Parse(s), "xs:time", s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:time")
            },
            ItemType.Base64Binary => value switch
            {
                PhoenixmlDb.Xdm.XdmValue v when v.Type == PhoenixmlDb.Xdm.XdmType.Base64Binary => v,
                PhoenixmlDb.Xdm.XdmValue v when v.Type == PhoenixmlDb.Xdm.XdmType.HexBinary =>
                    PhoenixmlDb.Xdm.XdmValue.Base64Binary((byte[])v.RawValue!),
                string s => PhoenixmlDb.Xdm.XdmValue.Base64Binary(Convert.FromBase64String(s.Trim())),
                _ => throw new XQueryRuntimeException("FORG0001", $"Cannot cast {value.GetType().Name} to xs:base64Binary")
            },
            ItemType.HexBinary => value switch
            {
                PhoenixmlDb.Xdm.XdmValue v when v.Type == PhoenixmlDb.Xdm.XdmType.HexBinary => v,
                PhoenixmlDb.Xdm.XdmValue v when v.Type == PhoenixmlDb.Xdm.XdmType.Base64Binary =>
                    PhoenixmlDb.Xdm.XdmValue.HexBinary((byte[])v.RawValue!),
                string s => PhoenixmlDb.Xdm.XdmValue.HexBinary(Convert.FromHexString(s.Trim())),
                _ => throw new XQueryRuntimeException("FORG0001", $"Cannot cast {value.GetType().Name} to xs:hexBinary")
            },
            ItemType.GYear => value switch
            {
                Xdm.XsGYear g => g,
                Xdm.XsDateTime dt => new Xdm.XsGYear(FormatGYear(dt.EffectiveYear, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGYear(FormatGYear(d.EffectiveYear, d.Timezone)),
                string s => ParseGYear(s),
                _ => ParseGYear(value.ToString()!.Trim())
            },
            ItemType.GYearMonth => value switch
            {
                Xdm.XsGYearMonth g => g,
                Xdm.XsDateTime dt => new Xdm.XsGYearMonth(FormatGYearMonth(dt.EffectiveYear, dt.Value.Month, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGYearMonth(FormatGYearMonth(d.EffectiveYear, d.Date.Month, d.Timezone)),
                string s => ParseGYearMonth(s),
                _ => ParseGYearMonth(value.ToString()!.Trim())
            },
            ItemType.GMonthDay => value switch
            {
                Xdm.XsGMonthDay g => g,
                Xdm.XsDateTime dt => new Xdm.XsGMonthDay(FormatGMonthDay(dt.Value.Month, dt.Value.Day, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGMonthDay(FormatGMonthDay(d.Date.Month, d.Date.Day, d.Timezone)),
                string s => ParseGMonthDay(s),
                _ => ParseGMonthDay(value.ToString()!.Trim())
            },
            ItemType.GDay => value switch
            {
                Xdm.XsGDay g => g,
                Xdm.XsDateTime dt => new Xdm.XsGDay(FormatGDay(dt.Value.Day, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGDay(FormatGDay(d.Date.Day, d.Timezone)),
                string s => ParseGDay(s),
                _ => ParseGDay(value.ToString()!.Trim())
            },
            ItemType.GMonth => value switch
            {
                Xdm.XsGMonth g => g,
                Xdm.XsDateTime dt => new Xdm.XsGMonth(FormatGMonth(dt.Value.Month, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGMonth(FormatGMonth(d.Date.Month, d.Timezone)),
                string s => ParseGMonth(s),
                _ => ParseGMonth(value.ToString()!.Trim())
            },
            _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast to type {targetType}")
        };
    }

    private static TimeSpan ParseDayTimeDuration(string s)
    {
        var trimmed = s.Trim();
        var check = trimmed.StartsWith('-') ? trimmed[1..] : trimmed;
        if (check.StartsWith('P'))
        {
            var afterP = check[1..];
            var tIdx = afterP.IndexOf('T', StringComparison.Ordinal);
            // The part before 'T' (date part) must only contain digits and 'D' — no 'Y' or 'M'
            var datePart = tIdx >= 0 ? afterP[..tIdx] : afterP;
            if (datePart.Contains('Y', StringComparison.Ordinal) || datePart.Contains('M', StringComparison.Ordinal))
                throw new FormatException($"Invalid dayTimeDuration: contains year/month components");
        }
        return System.Xml.XmlConvert.ToTimeSpan(trimmed);
    }

    public static Xdm.XsTypedString NormalizeStringSubtype(string value, string typeName)
    {
        // XSD whitespace facets: normalizedString = "replace", others = "collapse"
        string normalized = typeName switch
        {
            "normalizedString" => ReplaceWs(value),
            "token" or "language" or "Name" or "NCName" or "NMTOKEN"
                or "ID" or "IDREF" or "ENTITY" => CollapseWs(value),
            _ => value
        };
        // Lexical validation
        bool ok = typeName switch
        {
            "language" => System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$"),
            "Name" => IsValidXmlName(normalized),
            "NCName" or "ID" or "IDREF" or "ENTITY" => IsValidNCNameLex(normalized),
            "NMTOKEN" => IsValidNmtoken(normalized),
            _ => true
        };
        if (!ok)
            throw new XQueryRuntimeException("FORG0001", $"'{value}' is not a valid xs:{typeName}");
        return new Xdm.XsTypedString(normalized, typeName);
    }

    private static string ReplaceWs(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c is '\t' or '\n' or '\r' ? ' ' : c);
        return sb.ToString();
    }

    private static string CollapseWs(string s)
    {
        var replaced = ReplaceWs(s);
        // Collapse consecutive spaces and trim
        var sb = new System.Text.StringBuilder(replaced.Length);
        bool prevSpace = true; // trim leading
        foreach (var c in replaced)
        {
            if (c == ' ')
            {
                if (!prevSpace) { sb.Append(' '); prevSpace = true; }
            }
            else { sb.Append(c); prevSpace = false; }
        }
        // trim trailing
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.ToString();
    }

    internal static bool IsStringSubtype(string typeName) => typeName is
        "normalizedString" or "token" or "language" or "NMTOKEN"
        or "Name" or "NCName" or "ID" or "IDREF" or "ENTITY";

    internal static bool IsValidNCNameLex(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
        for (int i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) return false;
        }
        return true;
    }

    private static bool IsValidXmlName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_' || s[0] == ':')) return false;
        for (int i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ':')) return false;
        }
        return true;
    }

    private static bool IsValidNmtoken(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ':')) return false;
        return true;
    }

    public static void ValidateIntegerSubtype(long value, string typeName)
    {
        bool valid = typeName switch
        {
            "long" => true, // xs:long = full long range
            "int" => value >= int.MinValue && value <= int.MaxValue,
            "short" => value >= short.MinValue && value <= short.MaxValue,
            "byte" => value >= sbyte.MinValue && value <= sbyte.MaxValue,
            "unsignedLong" => value >= 0,
            "unsignedInt" => value >= 0 && value <= uint.MaxValue,
            "unsignedShort" => value >= 0 && value <= ushort.MaxValue,
            "unsignedByte" => value >= 0 && value <= byte.MaxValue,
            "positiveInteger" => value > 0,
            "nonNegativeInteger" => value >= 0,
            "negativeInteger" => value < 0,
            "nonPositiveInteger" => value <= 0,
            _ => true
        };
        if (!valid)
            throw new XQueryRuntimeException("FORG0001",
                $"Value {value} is out of range for xs:{typeName}");
    }

    /// <summary>
    /// Validates an out-of-long-range BigInteger value against an xs:integer subtype.
    /// Only xs:integer (unbounded), xs:nonNegativeInteger, xs:nonPositiveInteger,
    /// xs:positiveInteger, xs:negativeInteger, and xs:unsignedLong admit values outside
    /// the long range. All bounded long-or-smaller subtypes reject such values.
    /// </summary>
    public static void ValidateIntegerSubtype(BigInteger value, string typeName)
    {
        // The unbounded xs:integer subtypes — only their sign matters.
        bool valid = typeName switch
        {
            "integer" => true,
            "nonNegativeInteger" => value >= 0,
            "positiveInteger" => value > 0,
            "nonPositiveInteger" => value <= 0,
            "negativeInteger" => value < 0,
            // xs:unsignedLong: 0 ≤ value ≤ 2^64 - 1. BigInteger can exceed long.MaxValue for
            // values in (long.MaxValue, 2^64 - 1].
            "unsignedLong" => value >= 0 && value <= ulong.MaxValue,
            // All other subtypes (long, int, short, byte, unsignedInt, unsignedShort, unsignedByte)
            // are fully bounded within long.Range — any BigInteger outside long.Range is invalid.
            _ => false
        };
        if (!valid)
            throw new XQueryRuntimeException("FORG0001",
                $"Value {value} is out of range for xs:{typeName}");
    }

    private static void RejectInvalidCast(object value, ItemType target)
    {
        // String can cast to anything — no rejection
        if (value is string) return;
        // AnyAtomicType accepts all atomic values
        if (target == ItemType.AnyAtomicType) return;
        // Numeric → non-numeric/non-string targets (except boolean)
        bool isNumeric = value is int or long or double or float or decimal or BigInteger;
        if (isNumeric && target is ItemType.Duration or ItemType.YearMonthDuration
            or ItemType.DayTimeDuration or ItemType.DateTime or ItemType.Date or ItemType.Time
            or ItemType.GYear or ItemType.GYearMonth or ItemType.GMonthDay or ItemType.GDay
            or ItemType.GMonth or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to {target}");
        // Boolean → date/time/duration/binary targets
        if (value is bool && target is ItemType.Duration or ItemType.YearMonthDuration
            or ItemType.DayTimeDuration or ItemType.DateTime or ItemType.Date or ItemType.Time
            or ItemType.GYear or ItemType.GYearMonth or ItemType.GMonthDay or ItemType.GDay
            or ItemType.GMonth or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast xs:boolean to {target}");
        // Duration → numeric/boolean/QName/anyURI/binary targets
        bool isDuration = value is TimeSpan or Xdm.YearMonthDuration or Xdm.XsDuration;
        if (isDuration && target is ItemType.Integer or ItemType.Double or ItemType.Float
            or ItemType.Decimal or ItemType.Boolean or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary
            or ItemType.DateTime or ItemType.Date or ItemType.Time
            or ItemType.GYear or ItemType.GYearMonth or ItemType.GMonthDay or ItemType.GDay or ItemType.GMonth)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast duration to {target}");
        // Date/time → numeric/boolean/QName/anyURI/binary/duration targets
        bool isDateTime = value is Xdm.XsDateTime or Xdm.XsDate or Xdm.XsTime
            or DateTimeOffset or DateOnly or TimeOnly;
        if (isDateTime && target is ItemType.Integer or ItemType.Double or ItemType.Float
            or ItemType.Decimal or ItemType.Boolean or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary
            or ItemType.Duration or ItemType.YearMonthDuration or ItemType.DayTimeDuration)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast date/time to {target}");
        // QName → anything except string/untypedAtomic
        if (value is QName && target is not (ItemType.String or ItemType.UntypedAtomic or ItemType.QName))
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast xs:QName to {target}");
        // anyURI → only string/untypedAtomic/anyURI
        if (value is Xdm.XsAnyUri && target is not (ItemType.String or ItemType.UntypedAtomic or ItemType.AnyUri))
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast xs:anyURI to {target}");
        // g-types → only string/untypedAtomic/same-type/date/dateTime
        bool isGType = value is Xdm.XsGYear or Xdm.XsGYearMonth or Xdm.XsGMonthDay or Xdm.XsGDay or Xdm.XsGMonth;
        if (isGType && target is ItemType.Integer or ItemType.Double or ItemType.Float
            or ItemType.Decimal or ItemType.Boolean or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary
            or ItemType.Duration or ItemType.YearMonthDuration or ItemType.DayTimeDuration
            or ItemType.DateTime or ItemType.Date or ItemType.Time)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast g-type to {target}");
        // base64Binary/hexBinary → only string/untypedAtomic/base64Binary/hexBinary
        bool isBinary = value is PhoenixmlDb.Xdm.XdmValue xv2
            && (xv2.Type == PhoenixmlDb.Xdm.XdmType.Base64Binary || xv2.Type == PhoenixmlDb.Xdm.XdmType.HexBinary);
        if (isBinary && target is not (ItemType.String or ItemType.UntypedAtomic
            or ItemType.Base64Binary or ItemType.HexBinary or ItemType.AnyAtomicType))
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast binary to {target}");
    }

    private static void ValidateGTypeYear(string s)
    {
        // Extract the year digits and check for overflow
        var start = s.StartsWith('-') ? 1 : 0;
        var end = start;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        var yearStr = s[start..end];
        if (yearStr.Length > 9 || (yearStr.Length >= 5 && !long.TryParse(yearStr, out var y)))
            throw new XQueryRuntimeException("FODT0001", $"Overflow in xs:gYear value: '{s}'");
        // Per XSD 1.1, year 0000 represents 1 BCE and is valid. XSD 1.0 excluded it; we follow
        // XSD 1.1 since that is the XQuery/XPath 3.1+ default. See QT3 cbcl-castable-gYear-002..
    }

    private static void ValidateGTypeMonth(string monthStr)
    {
        if (int.TryParse(monthStr, out var m) && (m < 1 || m > 12))
            throw new XQueryRuntimeException("FORG0001", $"Invalid month: {monthStr}");
    }

    private static void ValidateGTypeDay(string dayStr)
    {
        if (int.TryParse(dayStr, out var d) && (d < 1 || d > 31))
            throw new XQueryRuntimeException("FORG0001", $"Invalid day: {dayStr}");
    }

    private static string FormatTz(DateTimeOffset dto, bool hasTz) =>
        hasTz ? (dto.Offset == TimeSpan.Zero ? "Z" : dto.ToString("zzz", System.Globalization.CultureInfo.InvariantCulture)) : "";

    private static string FormatGYear(long year, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(16);
        if (year < 0) { sb.Append('-'); sb.Append((-year).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)); }
        else sb.Append(year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
    private static string FormatGYearMonth(long year, int month, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(20);
        if (year < 0) { sb.Append('-'); sb.Append((-year).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)); }
        else sb.Append(year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('-');
        sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
    private static string FormatGMonthDay(int month, int day, TimeSpan? tz)
    { var sb = new System.Text.StringBuilder(16); sb.Append("--"); sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)); sb.Append('-'); sb.Append(day.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)); Xdm.XsDate.AppendTimezone(sb, tz); return sb.ToString(); }
    private static string FormatGDay(int day, TimeSpan? tz)
    { var sb = new System.Text.StringBuilder(12); sb.Append("---"); sb.Append(day.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)); Xdm.XsDate.AppendTimezone(sb, tz); return sb.ToString(); }
    private static string FormatGMonth(int month, TimeSpan? tz)
    { var sb = new System.Text.StringBuilder(12); sb.Append("--"); sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)); Xdm.XsDate.AppendTimezone(sb, tz); return sb.ToString(); }

    // gYearMonth: -?YYYY-MM(Z|(+|-)hh:mm)?
    private static Xdm.XsGYearMonth ParseGYearMonth(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d{4,}-\d{2}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gYearMonth: '{s}'");
        ValidateGTypeYear(s);
        // Validate month 01-12
        var dashIdx = s.StartsWith('-') ? s.IndexOf('-', 1) : s.IndexOf('-');
        if (dashIdx > 0)
        {
            var monthStr = s.Substring(dashIdx + 1, 2);
            if (int.TryParse(monthStr, out var month) && (month < 1 || month > 12))
                throw new XQueryRuntimeException("FORG0001", $"Invalid month in xs:gYearMonth: '{s}'");
        }
        // XSD 1.1 canonical form: "-0000-MM" → "0000-MM" (year 0 has no sign)
        if (s.StartsWith("-0000-", StringComparison.Ordinal))
            s = s[1..];
        return new Xdm.XsGYearMonth(s);
    }

    // gYear: -?YYYY(Z|(+|-)hh:mm)?
    private static Xdm.XsGYear ParseGYear(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d{4,}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gYear: '{s}'");
        // Validate year is in representable range
        ValidateGTypeYear(s);
        // XSD 1.1 canonical form: "-0000(...)" → "0000(...)" (year 0 has no sign)
        if (s.StartsWith("-0000", StringComparison.Ordinal))
            s = s[1..];
        return new Xdm.XsGYear(s);
    }

    // gMonthDay: --MM-DD(Z|(+|-)hh:mm)?
    private static Xdm.XsGMonthDay ParseGMonthDay(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^--\d{2}-\d{2}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gMonthDay: '{s}'");
        ValidateGTypeMonth(s[2..4]);
        ValidateGTypeDay(s[5..7]);
        return new Xdm.XsGMonthDay(s);
    }

    // gDay: ---DD(Z|(+|-)hh:mm)?
    private static Xdm.XsGDay ParseGDay(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^---\d{2}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gDay: '{s}'");
        ValidateGTypeDay(s[3..5]);
        return new Xdm.XsGDay(s);
    }

    // gMonth: --MM(Z|(+|-)hh:mm)?
    private static Xdm.XsGMonth ParseGMonth(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^--\d{2}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gMonth: '{s}'");
        return new Xdm.XsGMonth(s);
    }

    private static QName ParseQName(string s)
    {
        // Per XML Namespaces, a lexical QName is either an NCName or prefix:NCName.
        // The validation here rejects non-QName strings (e.g. "aGVsbG8=") so that
        // `castable as xs:QName` returns false for invalid lexical forms.
        var trimmed = s.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new XQueryRuntimeException("FORG0001", "Cannot cast empty string to xs:QName");

        var colonIdx = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx > 0)
        {
            var prefix = trimmed[..colonIdx];
            var localName = trimmed[(colonIdx + 1)..];
            if (!IsValidNCNameLex(prefix) || !IsValidNCNameLex(localName))
                throw new XQueryRuntimeException("FORG0001",
                    $"'{s}' is not a valid lexical xs:QName");
            var nsId = new NamespaceId((uint)Math.Abs(prefix.GetHashCode()));
            return new QName(nsId, localName, prefix);
        }
        if (!IsValidNCNameLex(trimmed))
            throw new XQueryRuntimeException("FORG0001",
                $"'{s}' is not a valid lexical xs:QName");
        return new QName(NamespaceId.None, trimmed);
    }

    public static bool MatchesType(IReadOnlyList<object?> items, XdmSequenceType type,
        ISchemaProvider? schemaProvider = null)
    {
        // Check occurrence
        var count = items.Count;
        switch (type.Occurrence)
        {
            case Occurrence.ExactlyOne when count != 1:
                return false;
            case Occurrence.ZeroOrOne when count > 1:
                return false;
            case Occurrence.OneOrMore when count < 1:
                return false;
            case Occurrence.ZeroOrMore:
                break;
        }

        if (type.ItemType == ItemType.Item)
            return true;

        // Check each item matches the type
        foreach (var item in items)
        {
            if (item == null)
            {
                if (type.Occurrence == Occurrence.ExactlyOne || type.Occurrence == Occurrence.OneOrMore)
                    return false;
                continue;
            }

            if (!MatchesItemType(item, type.ItemType))
                return false;

            // Check derived integer subtype range
            if (type.DerivedIntegerType != null && type.ItemType == ItemType.Integer)
            {
                if (!MatchesDerivedIntegerRange(item, type.DerivedIntegerType))
                    return false;
            }

            // Check derived string subtype hierarchy (xs:normalizedString, xs:token, xs:NCName, etc.).
            // Use LocalTypeName so xs:NCName (prefixed) and bare NCName (under xpath-default-namespace)
            // both run the subtype check.
            var stringLocalName = type.LocalTypeName ?? type.UnprefixedTypeName;
            if (stringLocalName != null && type.ItemType == ItemType.String
                && IsStringSubtype(stringLocalName))
            {
                if (item is Xdm.XsTypedString ts)
                {
                    if (!ts.IsSubtypeOf(stringLocalName))
                        return false;
                }
                else
                {
                    // Plain string is xs:string — not a subtype of any derived string type
                    return false;
                }
            }

            // Check typed function type: function(ParamTypes) as ReturnType
            // Uses contravariant parameter types and covariant return type
            if (type.FunctionParameterTypes != null && item is XQueryFunction fn)
            {
                if (!MatchesFunctionType(fn, type.FunctionParameterTypes, type.FunctionReturnType))
                    return false;
            }
            // Map checked against function(K) as V: per XPath 3.1 §2.5.4.2
            // A map matches function(K) as V iff arity=1 AND the return type V can accommodate
            // the empty sequence (since looking up a non-existent key returns ()).
            // We also check that all actual values in the map match V.
            else if (type.FunctionParameterTypes != null && item is IDictionary<object, object?> fnMap)
            {
                if (type.FunctionParameterTypes.Count != 1)
                    return false;
                // The return type must allow empty sequence (map may return () for unknown keys)
                if (type.FunctionReturnType != null)
                {
                    var retOcc = type.FunctionReturnType.Occurrence;
                    if (retOcc == Occurrence.ExactlyOne || retOcc == Occurrence.OneOrMore)
                        return false;
                    // Check all actual values match the return type
                    foreach (var kvp in fnMap)
                    {
                        var valItems = NormalizeToList(kvp.Value);
                        if (!MatchesType(valItems, type.FunctionReturnType))
                            return false;
                    }
                }
            }
            // Array checked against function(xs:integer) as V: arity=1, every member matches V
            else if (type.FunctionParameterTypes != null && item is List<object?> fnArr)
            {
                if (type.FunctionParameterTypes.Count != 1)
                    return false;
                // Array is function(xs:integer) as V — param must be compatible with xs:integer
                if (type.FunctionReturnType != null)
                {
                    foreach (var member in fnArr)
                    {
                        var memberItems = NormalizeToList(member);
                        if (!MatchesType(memberItems, type.FunctionReturnType))
                            return false;
                    }
                }
            }

            // Check parameterized map type: map(KeyType, ValueType)
            // Every key must match KeyType and every value must match ValueType
            if (type.MapKeyType != null && item is IDictionary<object, object?> mapDict)
            {
                foreach (var kvp in mapDict)
                {
                    if (!MatchesItemType(kvp.Key, type.MapKeyType.Value))
                        return false;
                    if (type.MapValueSequenceType != null)
                    {
                        var valItems = NormalizeToList(kvp.Value);
                        if (!MatchesType(valItems, type.MapValueSequenceType))
                            return false;
                    }
                    else if (type.MapValueType != null)
                    {
                        // Legacy: simple ItemType-only value check
                        if (kvp.Value == null || !MatchesItemType(kvp.Value, type.MapValueType.Value))
                            return false;
                    }
                }
            }

            // Check parameterized array type: array(MemberType)
            // Every member of the array must match MemberType
            if (type.ArrayMemberType != null && item is List<object?> arrayList)
            {
                foreach (var member in arrayList)
                {
                    var memberItems = NormalizeToList(member);
                    if (!MatchesType(memberItems, type.ArrayMemberType))
                        return false;
                }
            }

            // Check named element constraint: element(name) or element(name, type)
            if (type.ElementName != null && item is Xdm.Nodes.XdmElement elem)
            {
                if (elem.LocalName != type.ElementName)
                    return false;
            }

            // Check named attribute constraint: attribute(name) or attribute(name, type)
            if (type.AttributeName != null && item is Xdm.Nodes.XdmAttribute attr2)
            {
                if (attr2.LocalName != type.AttributeName)
                    return false;
            }

            // Check schema-element(name). Provider-aware: when supplied, route through
            // MatchesSchemaElement so substitution-group members and elements with schema
            // type annotations are recognized. Local-name fallback otherwise.
            if (type.SchemaElementName != null && item is Xdm.Nodes.XdmElement schemaElem2)
            {
                if (schemaProvider is not null)
                {
                    if (!schemaProvider.MatchesSchemaElement(schemaElem2,
                        type.SchemaElementNamespace ?? "", type.SchemaElementName))
                        return false;
                }
                else if (schemaElem2.LocalName != type.SchemaElementName)
                {
                    return false;
                }
            }

            // Check schema-attribute(name).
            if (type.SchemaAttributeName != null && item is Xdm.Nodes.XdmAttribute schemaAttr2)
            {
                if (schemaProvider is not null)
                {
                    if (!schemaProvider.MatchesSchemaAttribute(schemaAttr2,
                        type.SchemaAttributeNamespace ?? "", type.SchemaAttributeName))
                        return false;
                }
                else if (schemaAttr2.LocalName != type.SchemaAttributeName)
                {
                    return false;
                }
            }

            // Check document-node(element(name)) constraint
            if (type.DocumentElementName != null && item is Xdm.Nodes.XdmDocument doc)
            {
                if (doc.DocumentElementLocalName != type.DocumentElementName)
                    return false;
            }

            // Check type annotation for element(*, type) and attribute(*, type)
            if (type.TypeAnnotation is { } ta)
            {
                if (!MatchesTypeAnnotation(item, ta))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a function matches a typed function type using subtype rules:
    /// - Arity must match exactly
    /// - Parameter types are CONTRAVARIANT: function's declared param type must be
    ///   a subtype of (or equal to) the test's param type. This means the function
    ///   accepts at least everything the test type requires.
    /// - Return type is COVARIANT: function's declared return type must be a subtype
    ///   of (or equal to) the test's return type.
    /// </summary>
    public static bool MatchesFunctionType(XQueryFunction fn,
        IReadOnlyList<XdmSequenceType> requiredParamTypes, XdmSequenceType? requiredReturnType)
    {
        // Arity must match
        if (fn.Parameters.Count != requiredParamTypes.Count)
            return false;

        // Check parameter types (contravariant):
        // For each parameter position, the function's declared param type must be
        // a subtype of the required param type. i.e., the required type must be
        // a supertype of the function's type.
        for (int i = 0; i < requiredParamTypes.Count; i++)
        {
            var fnParamType = fn.Parameters[i].Type;
            var reqParamType = requiredParamTypes[i];

            // If the function's param type is the untyped default (item()*), accept:
            // inline function literals without declared parameter types are effectively wildcard.
            if (IsUntypedDefault(fnParamType))
                continue;

            // Contravariant: the required param type must be a subtype of the function's param type
            // (the function must accept everything the caller might pass)
            if (!IsSequenceTypeSubtypeOf(reqParamType, fnParamType))
                return false;
        }

        // Return type (covariant): the function's declared return type must be a subtype
        // of the required return type. Per XPath 3.1 §2.5.4, for instance-of checks,
        // the function's return type must be compatible.
        if (requiredReturnType != null && fn.ReturnType != null)
        {
            if (!IsSequenceTypeSubtypeOf(fn.ReturnType, requiredReturnType))
                return false;
        }

        return true;
    }

    private static bool IsUntypedDefault(XdmSequenceType t)
        => t.ItemType == ItemType.Item && t.Occurrence == Occurrence.ZeroOrMore
           && t.ElementName == null && t.AttributeName == null && t.TypeAnnotation == null
           && t.FunctionParameterTypes == null;

    /// <summary>
    /// Checks if type A is a subtype of type B (A subtype-of B).
    /// A is a subtype of B if every value that matches A also matches B.
    /// </summary>
    private static bool IsSequenceTypeSubtypeOf(XdmSequenceType subType, XdmSequenceType superType)
    {
        // Check occurrence compatibility: sub's occurrence must be "narrower" than super's
        if (!IsOccurrenceSubtypeOf(subType.Occurrence, superType.Occurrence))
            return false;

        // Check item type
        if (!IsItemTypeSubtypeOf(subType.ItemType, superType.ItemType))
            return false;

        // For element types: check name and type annotation constraints
        if (superType.ItemType == ItemType.Element)
        {
            // element(name) vs element(*): sub must have same or more specific name
            if (superType.ElementName != null && subType.ElementName != superType.ElementName)
                return false;
            // element(*, type) or element(name, type): sub must have a type annotation that derives-from
            // If super has a type annotation but sub doesn't, sub is NOT a subtype
            // (sub could match elements of any type, super requires specific type)
            if (superType.TypeAnnotation != null && subType.TypeAnnotation == null)
                return false;
        }

        // For attribute types: similar name/type annotation constraints
        if (superType.ItemType == ItemType.Attribute)
        {
            if (superType.AttributeName != null && subType.AttributeName != superType.AttributeName)
                return false;
            if (superType.TypeAnnotation != null && subType.TypeAnnotation == null)
                return false;
        }

        // For parameterized map types: map(K1,V1) subtype-of map(K2,V2) iff K1 subtype-of K2 AND V1 subtype-of V2
        if (superType.ItemType == ItemType.Map && superType.MapKeyType != null)
        {
            // sub is map(*) (no key type) — not a subtype of map(K,V)
            if (subType.MapKeyType == null)
                return false;
            if (!IsItemTypeSubtypeOf(subType.MapKeyType.Value, superType.MapKeyType.Value))
                return false;
            if (superType.MapValueSequenceType != null)
            {
                if (subType.MapValueSequenceType == null)
                    return false;
                if (!IsSequenceTypeSubtypeOf(subType.MapValueSequenceType, superType.MapValueSequenceType))
                    return false;
            }
        }

        // For parameterized array types: array(T1) subtype-of array(T2) iff T1 subtype-of T2
        if (superType.ItemType == ItemType.Array && superType.ArrayMemberType != null)
        {
            if (subType.ArrayMemberType == null)
                return false;
            if (!IsSequenceTypeSubtypeOf(subType.ArrayMemberType, superType.ArrayMemberType))
                return false;
        }

        // For typed function types: function(P1) as R1 subtype-of function(P2) as R2
        // iff P2 subtype-of P1 (contravariant) AND R1 subtype-of R2 (covariant)
        if (superType.FunctionParameterTypes != null)
        {
            // Handle map(K,V) subtype-of function(P) as R:
            // map(K,V) is equivalent to function(K) as V? for subtype purposes
            if (subType.FunctionParameterTypes == null && subType.ItemType == ItemType.Map
                && superType.FunctionParameterTypes.Count == 1)
            {
                // Map's key type must be a supertype of the required param (contravariant)
                if (subType.MapKeyType != null)
                {
                    var mapKeySeqType = new XdmSequenceType { ItemType = subType.MapKeyType.Value, Occurrence = Occurrence.ExactlyOne };
                    if (!IsSequenceTypeSubtypeOf(superType.FunctionParameterTypes[0], mapKeySeqType))
                        return false;
                }
                // Map's value type (as V?) must be a subtype of the required return type
                if (superType.FunctionReturnType != null && subType.MapValueSequenceType != null)
                {
                    // Map values may be empty (key not found), so effective return type is V?
                    var effectiveRetType = subType.MapValueSequenceType.Occurrence == Occurrence.ExactlyOne
                        ? new XdmSequenceType { ItemType = subType.MapValueSequenceType.ItemType, Occurrence = Occurrence.ZeroOrOne,
                            MapKeyType = subType.MapValueSequenceType.MapKeyType, MapValueSequenceType = subType.MapValueSequenceType.MapValueSequenceType,
                            ArrayMemberType = subType.MapValueSequenceType.ArrayMemberType, FunctionParameterTypes = subType.MapValueSequenceType.FunctionParameterTypes,
                            FunctionReturnType = subType.MapValueSequenceType.FunctionReturnType }
                        : subType.MapValueSequenceType;
                    if (!IsSequenceTypeSubtypeOf(effectiveRetType, superType.FunctionReturnType))
                        return false;
                }
            }
            // Handle array(T) subtype-of function(P) as R:
            // array(T) is equivalent to function(xs:integer) as T for subtype purposes
            else if (subType.FunctionParameterTypes == null && subType.ItemType == ItemType.Array
                     && superType.FunctionParameterTypes.Count == 1)
            {
                // Array param is xs:integer; check contravariance
                var arrayParamType = new XdmSequenceType { ItemType = ItemType.Integer, Occurrence = Occurrence.ExactlyOne };
                if (!IsSequenceTypeSubtypeOf(superType.FunctionParameterTypes[0], arrayParamType))
                    return false;
                if (superType.FunctionReturnType != null && subType.ArrayMemberType != null)
                {
                    if (!IsSequenceTypeSubtypeOf(subType.ArrayMemberType, superType.FunctionReturnType))
                        return false;
                }
            }
            else
            {
                if (subType.FunctionParameterTypes == null)
                    return false;
                if (subType.FunctionParameterTypes.Count != superType.FunctionParameterTypes.Count)
                    return false;
                for (int i = 0; i < superType.FunctionParameterTypes.Count; i++)
                {
                    // Contravariant: super's param must be subtype of sub's param
                    if (!IsSequenceTypeSubtypeOf(superType.FunctionParameterTypes[i], subType.FunctionParameterTypes[i]))
                        return false;
                }
                if (superType.FunctionReturnType != null)
                {
                    if (subType.FunctionReturnType == null)
                        return false;
                    if (!IsSequenceTypeSubtypeOf(subType.FunctionReturnType, superType.FunctionReturnType))
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if occurrence A is a sub-occurrence of B.
    /// ExactlyOne is subtype of ZeroOrOne, OneOrMore, ZeroOrMore, etc.
    /// </summary>
    private static bool IsOccurrenceSubtypeOf(Occurrence sub, Occurrence super)
    {
        if (sub == super) return true;
        return super switch
        {
            Occurrence.ZeroOrMore => true, // everything is subtype of *
            Occurrence.ZeroOrOne => sub == Occurrence.ExactlyOne || sub == Occurrence.Zero,
            Occurrence.OneOrMore => sub == Occurrence.ExactlyOne,
            Occurrence.ExactlyOne => false, // only ExactlyOne itself (handled by == above)
            Occurrence.Zero => false, // only Zero itself
            _ => false
        };
    }

    /// <summary>
    /// Checks if item type A is a subtype of item type B in the XDM type hierarchy.
    /// </summary>
    private static bool IsItemTypeSubtypeOf(ItemType sub, ItemType super)
    {
        if (sub == super) return true;
        if (super == ItemType.Item) return true; // item() is the top type

        return super switch
        {
            // xs:decimal supertypes: xs:integer
            ItemType.Decimal => sub is ItemType.Integer,
            // xs:double supertypes: (numeric promotion, not strict subtyping)
            // xs:anyAtomicType supertypes: all atomic types
            ItemType.AnyAtomicType => sub is ItemType.String or ItemType.Integer or ItemType.Double
                or ItemType.Float or ItemType.Decimal or ItemType.Boolean or ItemType.Date
                or ItemType.DateTime or ItemType.Time or ItemType.Duration or ItemType.YearMonthDuration
                or ItemType.DayTimeDuration or ItemType.QName or ItemType.AnyUri
                or ItemType.UntypedAtomic or ItemType.GYearMonth or ItemType.GYear
                or ItemType.GMonthDay or ItemType.GDay or ItemType.GMonth
                or ItemType.HexBinary or ItemType.Base64Binary,
            // xs:duration supertypes: yearMonthDuration, dayTimeDuration
            ItemType.Duration => sub is ItemType.YearMonthDuration or ItemType.DayTimeDuration,
            // node() supertypes: all node types
            ItemType.Node => sub is ItemType.Element or ItemType.Attribute or ItemType.Text
                or ItemType.Document or ItemType.Comment or ItemType.ProcessingInstruction,
            // function() supertypes: map(*), array(*)
            ItemType.Function => sub is ItemType.Map or ItemType.Array,
            // xs:string subtypes: (xs:NCName, xs:token, etc. — not tracked in our type system)
            _ => false
        };
    }

    /// <summary>
    /// Checks if an item's type annotation matches the required type, considering XSD type hierarchy.
    /// For non-schema-aware processors:
    /// Normalizes a value (which may be null, a single item, or a sequence) into a list for type checking.
    /// </summary>
    internal static IReadOnlyList<object?> NormalizeToList(object? value)
    {
        if (value == null) return Array.Empty<object?>();
        if (value is IEnumerable<object?> seq && value is not string && value is not Dictionary<object, object?>
            && value is not List<object?> && value is not PhoenixmlDb.Xdm.Nodes.XdmNode
            && value is not PhoenixmlDb.Xdm.TextNodeItem)
            return seq.ToList();
        return new[] { value };
    }

    /// <summary>
    /// - Elements have type annotation xs:untyped
    /// - Attributes have type annotation xs:untypedAtomic
    /// </summary>
    private static bool MatchesTypeAnnotation(object item, Xdm.XdmTypeName requiredType)
    {
        // xs:anyType matches everything (top of type hierarchy)
        if (requiredType == Xdm.XdmTypeName.AnyType)
            return true;

        if (item is Xdm.Nodes.XdmElement elem)
        {
            // Non-schema-aware: element type is xs:untyped
            // Type hierarchy: xs:untyped → xs:anyType
            return elem.TypeAnnotation == requiredType;
        }
        if (item is Xdm.Nodes.XdmAttribute attr)
        {
            // Non-schema-aware: attribute type is xs:untypedAtomic
            // Type hierarchy: xs:untypedAtomic → xs:anyAtomicType → xs:anySimpleType → xs:anyType
            if (attr.TypeAnnotation == requiredType)
                return true;
            return IsSubtypeOf(attr.TypeAnnotation, requiredType);
        }
        return false;
    }

    /// <summary>
    /// Checks if actualType is a subtype of requiredType in the XSD type hierarchy.
    /// </summary>
    private static bool IsSubtypeOf(Xdm.XdmTypeName actualType, Xdm.XdmTypeName requiredType)
    {
        // xs:untypedAtomic → xs:anyAtomicType → xs:anySimpleType → xs:anyType
        if (actualType == Xdm.XdmTypeName.UntypedAtomic)
        {
            return requiredType == Xdm.XdmTypeName.AnyAtomicType
                || requiredType == Xdm.XdmTypeName.AnySimpleType
                || requiredType == Xdm.XdmTypeName.AnyType;
        }
        // xs:untyped → xs:anyType
        if (actualType == Xdm.XdmTypeName.Untyped)
        {
            return requiredType == Xdm.XdmTypeName.AnyType;
        }
        return false;
    }

    public static bool MatchesDerivedIntegerRange(object? item, string derivedType)
    {
        BigInteger val;
        if (item is long l) val = l;
        else if (item is int i) val = i;
        else if (item is BigInteger bi) val = bi;
        else return false;

        return derivedType switch
        {
            "long" => val >= long.MinValue && val <= long.MaxValue,
            "int" => val >= int.MinValue && val <= int.MaxValue,
            "short" => val >= short.MinValue && val <= short.MaxValue,
            "byte" => val >= sbyte.MinValue && val <= sbyte.MaxValue,
            "unsignedLong" => val >= 0 && val <= ulong.MaxValue,
            "unsignedInt" => val >= 0 && val <= uint.MaxValue,
            "unsignedShort" => val >= 0 && val <= ushort.MaxValue,
            "unsignedByte" => val >= 0 && val <= byte.MaxValue,
            "nonNegativeInteger" => val >= 0,
            "positiveInteger" => val > 0,
            "nonPositiveInteger" => val <= 0,
            "negativeInteger" => val < 0,
            _ => true
        };
    }

    public static bool MatchesItemType(object? item, ItemType type)
    {
        if (item == null)
            return false;
        return type switch
        {
            ItemType.Item => true,
            ItemType.AnyAtomicType => item is not PhoenixmlDb.Xdm.Nodes.XdmNode and not PhoenixmlDb.Xdm.TextNodeItem and not XQueryFunction and not Dictionary<object, object?>,
            ItemType.String => item is string or Xdm.XsTypedString,
            ItemType.Integer => item is int or long or BigInteger,
            ItemType.Double => item is double,
            ItemType.Float => item is float,
            ItemType.Decimal => item is decimal or int or long or BigInteger,
            ItemType.Boolean => item is bool,
            ItemType.Date => item is Xdm.XsDate or DateOnly,
            ItemType.DateTime => item is Xdm.XsDateTime or DateTimeOffset,
            ItemType.Time => item is Xdm.XsTime or TimeOnly,
            ItemType.Duration => item is TimeSpan or Xdm.DayTimeDuration or Xdm.YearMonthDuration or Xdm.XsDuration,
            ItemType.YearMonthDuration => item is Xdm.YearMonthDuration,
            ItemType.DayTimeDuration => item is TimeSpan or Xdm.DayTimeDuration,
            ItemType.QName => item is PhoenixmlDb.Core.QName,
            ItemType.AnyUri => item is Xdm.XsAnyUri,
            ItemType.UntypedAtomic => item is Xdm.XsUntypedAtomic,
            ItemType.GYearMonth => item is Xdm.XsGYearMonth,
            ItemType.GYear => item is Xdm.XsGYear,
            ItemType.GMonthDay => item is Xdm.XsGMonthDay,
            ItemType.GDay => item is Xdm.XsGDay,
            ItemType.GMonth => item is Xdm.XsGMonth,
            ItemType.HexBinary => item is Xdm.XdmValue v1 && v1.Type == Xdm.XdmType.HexBinary,
            ItemType.Base64Binary => item is Xdm.XdmValue v2 && v2.Type == Xdm.XdmType.Base64Binary,
            ItemType.Node => item is PhoenixmlDb.Xdm.Nodes.XdmNode or PhoenixmlDb.Xdm.TextNodeItem,
            ItemType.Element => item is PhoenixmlDb.Xdm.Nodes.XdmElement,
            ItemType.Attribute => item is PhoenixmlDb.Xdm.Nodes.XdmAttribute,
            ItemType.Text => item is PhoenixmlDb.Xdm.Nodes.XdmText or PhoenixmlDb.Xdm.TextNodeItem,
            ItemType.Comment => item is PhoenixmlDb.Xdm.Nodes.XdmComment,
            ItemType.Document => item is PhoenixmlDb.Xdm.Nodes.XdmDocument,
            ItemType.ProcessingInstruction => item is PhoenixmlDb.Xdm.Nodes.XdmProcessingInstruction,
            ItemType.Function => item is XQueryFunction or IDictionary<object, object?> or List<object?>,
            ItemType.Map => item is Dictionary<object, object?> or IDictionary<object, object?>,
            ItemType.Array => item is List<object?>,
            ItemType.Record => item is Dictionary<object, object?> or IDictionary<object, object?>,
            ItemType.Enum => item is string,
            ItemType.Union => true, // Union matching done in MatchesSequenceItemType with member types
            ItemType.SchemaElement => item is PhoenixmlDb.Xdm.Nodes.XdmElement, // Full matching via ISchemaProvider
            ItemType.SchemaAttribute => item is PhoenixmlDb.Xdm.Nodes.XdmAttribute, // Full matching via ISchemaProvider
            ItemType.Notation => false, // xs:NOTATION — no atomic value is ever an instance
            ItemType.Error => false, // xs:error — the empty union type; no value is ever an instance
            _ => false
        };
    }

    /// <summary>
    /// Checks if an item matches a full sequence type (including named element/document constraints).
    /// When <paramref name="schemaProvider"/> is supplied, schema-element/schema-attribute names
    /// are matched through the provider — covering substitution groups and type-annotation
    /// subsumption. When null, falls back to local-name-only matching (legacy behavior, also
    /// what shows up at most call sites that don't have provider access in scope).
    /// </summary>
    public static bool MatchesSequenceItemType(object? item, XdmSequenceType seqType,
        ISchemaProvider? schemaProvider = null)
    {
        if (!MatchesItemType(item, seqType.ItemType))
            return false;

        // Check named element constraint: element(name)
        if (seqType.ElementName != null && item is PhoenixmlDb.Xdm.Nodes.XdmElement elem)
        {
            if (elem.LocalName != seqType.ElementName)
                return false;
        }

        // Check schema-element(name). With a provider, route through MatchesSchemaElement so
        // substitution-group members and elements with schema-derived type annotations match.
        // Without a provider, fall back to local-name comparison (best-effort).
        if (seqType.SchemaElementName != null && item is PhoenixmlDb.Xdm.Nodes.XdmElement schemaElem)
        {
            if (schemaProvider is not null)
            {
                if (!schemaProvider.MatchesSchemaElement(schemaElem,
                    seqType.SchemaElementNamespace ?? "", seqType.SchemaElementName))
                    return false;
            }
            else if (schemaElem.LocalName != seqType.SchemaElementName)
            {
                return false;
            }
        }

        // Check schema-attribute(name).
        if (seqType.SchemaAttributeName != null && item is PhoenixmlDb.Xdm.Nodes.XdmAttribute schemaAttr)
        {
            if (schemaProvider is not null)
            {
                if (!schemaProvider.MatchesSchemaAttribute(schemaAttr,
                    seqType.SchemaAttributeNamespace ?? "", seqType.SchemaAttributeName))
                    return false;
            }
            else if (schemaAttr.LocalName != seqType.SchemaAttributeName)
            {
                return false;
            }
        }

        // Check processing-instruction("name") constraint
        if (seqType.PIName != null && item is PhoenixmlDb.Xdm.Nodes.XdmProcessingInstruction pi)
        {
            if (pi.Target != seqType.PIName)
                return false;
        }

        // Check document-node(element(name)) constraint
        if (seqType.DocumentElementName != null && item is not PhoenixmlDb.Xdm.Nodes.XdmDocument)
        {
            return false;
        }

        // XPath 4.0: Check record field constraints
        if (seqType.RecordFields != null && item is IDictionary<object, object?> recordMap)
        {
            foreach (var (fieldName, fieldDef) in seqType.RecordFields)
            {
                var hasField = recordMap.ContainsKey(fieldName);
                if (!hasField && !fieldDef.Optional)
                    return false; // Required field missing
                if (hasField && fieldDef.Type != null)
                {
                    var fieldValue = recordMap[fieldName];
                    if (!MatchesSequenceItemType(fieldValue, fieldDef.Type))
                        return false; // Field type mismatch
                }
            }
            // If not extensible, check that no extra fields exist
            if (!seqType.RecordExtensible)
            {
                foreach (var key in recordMap.Keys)
                {
                    if (key is string keyStr && !seqType.RecordFields.ContainsKey(keyStr))
                        return false; // Extra field not allowed
                }
            }
        }

        // XPath 4.0: Check union type — item must match at least one member type
        if (seqType.UnionTypes != null)
        {
            return seqType.UnionTypes.Any(memberType => MatchesSequenceItemType(item, memberType));
        }

        // XPath 4.0: Check enum value constraint
        if (seqType.EnumValues != null && item is string strVal)
        {
            if (!seqType.EnumValues.Contains(strVal))
                return false; // Value not in enum
        }

        return true;
    }

    /// <summary>
    /// Validate a single argument against a declared parameter type for dynamic function calls.
    /// Raises XPTY0004 if the argument cannot be coerced to the parameter type.
    /// Function coercion rules (XPath 3.0 §3.1.5.1):
    ///   - untypedAtomic → cast to target type
    ///   - anyURI → string promotion
    ///   - numeric promotion: integer→decimal→float→double
    ///   - subtype substitution
    ///   - otherwise → XPTY0004
    /// </summary>
    public static void ValidateDynamicFunctionArg(object? arg, XdmSequenceType paramType, string funcName, int paramIndex)
    {
        if (paramType.ItemType == ItemType.Item || paramType.ItemType == ItemType.AnyAtomicType)
            return; // item() and xs:anyAtomicType accept anything

        // Handle sequences
        if (arg is object?[] arr)
        {
            if (arr.Length == 0 && paramType.Occurrence is Occurrence.ExactlyOne or Occurrence.OneOrMore)
                throw new XQueryRuntimeException("XPTY0004",
                    $"Function {funcName}(): parameter {paramIndex + 1} requires a value, got empty sequence");
            foreach (var item in arr)
                ValidateDynamicFunctionArgItem(item, paramType, funcName, paramIndex);
            return;
        }

        if (arg == null)
        {
            if (paramType.Occurrence is Occurrence.ExactlyOne or Occurrence.OneOrMore)
                throw new XQueryRuntimeException("XPTY0004",
                    $"Function {funcName}(): parameter {paramIndex + 1} requires a value, got empty sequence");
            return;
        }

        ValidateDynamicFunctionArgItem(arg, paramType, funcName, paramIndex);
    }

    private static void ValidateDynamicFunctionArgItem(object? item, XdmSequenceType paramType, string funcName, int paramIndex)
    {
        if (item == null) return;

        // untypedAtomic can be cast to any target type (coercion rule)
        if (item is Xdm.XsUntypedAtomic)
            return;
        if (item is Xdm.XsTypedString ts && ts.TypeName == "untypedAtomic")
            return;

        // Nodes passed to atomic-typed parameters will be atomized by the function call mechanism.
        // The atomized value is untypedAtomic (for untyped elements), which can be coerced to any type.
        // So nodes are always allowed when the target is an atomic type.
        if (item is Xdm.Nodes.XdmNode || item is Xdm.TextNodeItem)
        {
            // If the parameter expects a node or item type, check normally below.
            // If it expects an atomic type, allow it (atomization + untypedAtomic coercion will handle it).
            if (paramType.ItemType is not ItemType.Node and not ItemType.Element and not ItemType.Attribute
                and not ItemType.Text and not ItemType.Comment and not ItemType.ProcessingInstruction
                and not ItemType.Document and not ItemType.Item)
                return;
        }

        // Check if item already matches the declared type
        if (MatchesItemType(item, paramType.ItemType))
            return;

        // Numeric promotion: integer → decimal → float → double
        if (paramType.ItemType == ItemType.Double && (item is int or long or BigInteger or decimal or float))
            return;
        if (paramType.ItemType == ItemType.Float && (item is int or long or BigInteger or decimal))
            return;
        if (paramType.ItemType == ItemType.Decimal && (item is int or long or BigInteger))
            return;

        // anyURI → string promotion
        if (paramType.ItemType == ItemType.String && item is Xdm.XsAnyUri)
            return;
        if (paramType.ItemType == ItemType.String && item is Xdm.XsTypedString uts && uts.TypeName == "anyURI")
            return;

        // Node subtype: element/attribute/text/etc. are subtypes of node
        if (paramType.ItemType == ItemType.Node && item is Xdm.Nodes.XdmNode)
            return;

        // Function types: function items match function(*)
        if (paramType.ItemType == ItemType.Function && item is XQueryFunction)
            return;

        // Map: maps match map(*)
        if (paramType.ItemType == ItemType.Map && item is IDictionary<object, object?>)
            return;

        // Array: arrays match array(*)
        if (paramType.ItemType == ItemType.Array && item is IList<object?>)
            return;

        throw new XQueryRuntimeException("XPTY0004",
            $"Function {funcName}(): argument {paramIndex + 1} of type {item.GetType().Name} does not match required type {paramType}");
    }

    public static bool DeepEquals(object? a, object? b, StringComparison stringComparison = StringComparison.Ordinal,
        INodeProvider? nodeProvider = null)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
            return false;

        // Unwrap XsTypedString to plain string for comparison purposes
        if (a is Xdm.XsTypedString tsA) a = tsA.Value;
        if (b is Xdm.XsTypedString tsB) b = tsB.Value;

        // XDM Node deep-equal: compare by node kind, name, and content per XPath spec §15.3.1
        if (a is XdmNode nodeA && b is XdmNode nodeB)
            return DeepEqualsNodes(nodeA, nodeB, stringComparison, nodeProvider);

        // Node vs non-node: never equal
        if (a is XdmNode || b is XdmNode)
            return false;

        // XPath deep-equal requires same primitive type for atomic values.
        // Numeric types (int, long, decimal, double, float) are all comparable.
        // Strings are only comparable with strings. Different primitive types → false.
        var aIsNumeric = a is int or long or double or float or decimal or BigInteger;
        var bIsNumeric = b is int or long or double or float or decimal or BigInteger;

        if (aIsNumeric && bIsNumeric)
        {
            // BigInteger comparison — avoid double precision loss
            if (a is BigInteger || b is BigInteger)
            {
                if (a is double or float || b is double or float)
                {
                    var x = Convert.ToDouble(a);
                    var y = Convert.ToDouble(b);
                    if (double.IsNaN(x) && double.IsNaN(y)) return true;
                    return x == y;
                }
                var abi = a is BigInteger ba ? ba : (BigInteger)Convert.ToInt64(a);
                var bbi = b is BigInteger bb ? bb : (BigInteger)Convert.ToInt64(b);
                return abi == bbi;
            }
            // Per XPath F&O §15.3.1: if either is float (not double), compare as float.
            // If either is double, compare as double. Otherwise compare as decimal.
            bool aIsFloat = a is float;
            bool bIsFloat = b is float;
            bool aIsDouble = a is double;
            bool bIsDouble = b is double;
            if ((aIsFloat || bIsFloat) && !aIsDouble && !bIsDouble)
            {
                var fa = Convert.ToSingle(a);
                var fb = Convert.ToSingle(b);
                if (float.IsNaN(fa) && float.IsNaN(fb)) return true;
                return fa == fb;
            }
            var da = Convert.ToDouble(a);
            var db = Convert.ToDouble(b);
            // deep-equal considers NaN equal to NaN (unlike value comparison)
            if (double.IsNaN(da) && double.IsNaN(db)) return true;
            return da == db;
        }

        if (aIsNumeric != bIsNumeric)
            return false; // One numeric, other not — different primitive types

        // Both are non-numeric: compare by type then value
        if (a is bool && b is bool)
            return (bool)a == (bool)b;
        if (a is bool || b is bool)
            return false;

        // Namespace nodes: equal if same prefix and same URI
        if (a is XdmNamespace nsA && b is XdmNamespace nsB)
            return string.Equals(nsA.Prefix, nsB.Prefix, StringComparison.Ordinal)
                && string.Equals(nsA.Uri, nsB.Uri, stringComparison);
        if (a is XdmNamespace || b is XdmNamespace)
            return false;

        // Array deep-equal: same size + all members deep-equal
        if (a is List<object?> arrA && b is List<object?> arrB)
        {
            if (arrA.Count != arrB.Count) return false;
            for (int i = 0; i < arrA.Count; i++)
                if (!DeepEquals(arrA[i], arrB[i], stringComparison, nodeProvider)) return false;
            return true;
        }
        if (a is List<object?> || b is List<object?>)
            return false; // array vs non-map

        // Sequence deep-equal: array members can be sequences (object?[], string[], etc.)
        // Compare element-by-element. Note: List<object?> is XDM array (handled above).
        if (a is Array seqA && b is Array seqB)
        {
            if (seqA.Length != seqB.Length) return false;
            for (int i = 0; i < seqA.Length; i++)
                if (!DeepEquals(seqA.GetValue(i), seqB.GetValue(i), stringComparison, nodeProvider)) return false;
            return true;
        }
        // One is a sequence, other is not — not equal (unless single-item sequence matches the item)
        if (a is Array singleSeqA && singleSeqA.Length == 1)
            return DeepEquals(singleSeqA.GetValue(0), b, stringComparison, nodeProvider);
        if (b is Array singleSeqB && singleSeqB.Length == 1)
            return DeepEquals(a, singleSeqB.GetValue(0), stringComparison, nodeProvider);

        // Map deep-equal: same keys + same values for each key
        if (a is IDictionary<object, object?> mapA && b is IDictionary<object, object?> mapB)
        {
            if (mapA.Count != mapB.Count) return false;
            foreach (var kv in mapA)
            {
                if (!mapB.TryGetValue(kv.Key, out var bVal)) return false;
                if (!DeepEquals(kv.Value, bVal, stringComparison, nodeProvider)) return false;
            }
            return true;
        }
        if (a is IDictionary<object, object?> || b is IDictionary<object, object?>)
            return false; // map vs non-map

        // xs:anyURI and xs:string are comparable in deep-equal (XPath spec: anyURI is promotable to string)
        if ((a is Xdm.XsAnyUri || a is string) && (b is Xdm.XsAnyUri || b is string))
            return string.Equals(a.ToString(), b.ToString(), stringComparison);

        // xs:untypedAtomic and xs:string are comparable
        if ((a is Xdm.XsUntypedAtomic || a is string) && (b is Xdm.XsUntypedAtomic || b is string))
            return string.Equals(a.ToString(), b.ToString(), stringComparison);

        // Date/time types: different types are not deep-equal (e.g., xs:date vs xs:string)
        if (a.GetType() != b.GetType())
            return false;

        // Date/time value comparison using eq semantics (handles implicit timezone)
        if (a is Xdm.XsDateTime dtA && b is Xdm.XsDateTime dtB)
            return dtA.CompareTo(dtB) == 0;
        if (a is Xdm.XsDate dateA && b is Xdm.XsDate dateB)
            return dateA.CompareTo(dateB) == 0;
        if (a is Xdm.XsTime timeA && b is Xdm.XsTime timeB)
            return timeA.CompareTo(timeB) == 0;
        // Duration value comparison
        if (a is Xdm.XsDuration durA && b is Xdm.XsDuration durB)
            return durA.TotalMonths == durB.TotalMonths && durA.DayTime == durB.DayTime;
        if (a is Xdm.YearMonthDuration ymdA && b is Xdm.YearMonthDuration ymdB)
            return ymdA.TotalMonths == ymdB.TotalMonths;
        if (a is Xdm.DayTimeDuration dtdA && b is Xdm.DayTimeDuration dtdB)
            return dtdA.TotalSeconds == dtdB.TotalSeconds;

        return string.Equals(a.ToString(), b.ToString(), stringComparison);
    }

    private static bool DeepEqualsNodes(XdmNode a, XdmNode b, StringComparison stringComparison,
        INodeProvider? nodeProvider)
    {
        // Different node kinds → not equal
        if (a.NodeKind != b.NodeKind)
            return false;

        switch (a)
        {
            case XdmElement elemA when b is XdmElement elemB:
                // Elements: same name (namespace + local name)
                if (elemA.Namespace != elemB.Namespace || elemA.LocalName != elemB.LocalName)
                    return false;
                // Compare attributes (order-independent): same count, each attribute in A has match in B
                var attrsA = GetAttributeNodes(elemA, nodeProvider);
                var attrsB = GetAttributeNodes(elemB, nodeProvider);
                if (attrsA.Count != attrsB.Count)
                    return false;
                foreach (var attrA in attrsA)
                {
                    var matchB = attrsB.FirstOrDefault(ab =>
                        ab.Namespace == attrA.Namespace && ab.LocalName == attrA.LocalName);
                    if (matchB == null || !string.Equals(attrA.Value, matchB.Value, stringComparison))
                        return false;
                }
                // Compare children (order-dependent), skipping PIs and comments per XPath spec
                var childrenA = GetSignificantChildren(elemA, nodeProvider);
                var childrenB = GetSignificantChildren(elemB, nodeProvider);
                if (childrenA.Count != childrenB.Count)
                    return false;
                for (int i = 0; i < childrenA.Count; i++)
                    if (!DeepEqualsNodes(childrenA[i], childrenB[i], stringComparison, nodeProvider))
                        return false;
                return true;

            case XdmDocument docA when b is XdmDocument docB:
                // Documents: compare children, skipping PIs and comments per XPath spec
                var dChildrenA = GetSignificantChildren(docA, nodeProvider);
                var dChildrenB = GetSignificantChildren(docB, nodeProvider);
                if (dChildrenA.Count != dChildrenB.Count)
                    return false;
                for (int i = 0; i < dChildrenA.Count; i++)
                    if (!DeepEqualsNodes(dChildrenA[i], dChildrenB[i], stringComparison, nodeProvider))
                        return false;
                return true;

            case XdmAttribute attrA when b is XdmAttribute attrB:
                // Attributes: same name + same string value
                return attrA.Namespace == attrB.Namespace
                    && attrA.LocalName == attrB.LocalName
                    && string.Equals(attrA.Value, attrB.Value, stringComparison);

            case XdmProcessingInstruction piA when b is XdmProcessingInstruction piB:
                // PIs: same target + same value
                return string.Equals(piA.Target, piB.Target, StringComparison.Ordinal)
                    && string.Equals(piA.Value, piB.Value, stringComparison);

            case XdmText textA when b is XdmText textB:
                return string.Equals(textA.Value, textB.Value, stringComparison);

            case XdmComment commentA when b is XdmComment commentB:
                return string.Equals(commentA.Value, commentB.Value, stringComparison);

            default:
                // Same kind, compare string values
                return string.Equals(a.StringValue, b.StringValue, stringComparison);
        }
    }

    private static List<XdmAttribute> GetAttributeNodes(XdmElement elem, INodeProvider? nodeProvider)
    {
        var attrs = new List<XdmAttribute>();
        foreach (var attrId in elem.Attributes)
        {
            if (nodeProvider?.GetNode(attrId) is XdmAttribute attr)
                attrs.Add(attr);
        }
        return attrs;
    }

    /// <summary>
    /// Returns element/text children only, filtering out PIs and comments
    /// as required by the XPath deep-equal specification.
    /// </summary>
    private static List<XdmNode> GetSignificantChildren(XdmNode node, INodeProvider? nodeProvider)
    {
        var children = GetChildNodes(node, nodeProvider);
        children.RemoveAll(c => c is XdmProcessingInstruction or XdmComment);
        return children;
    }

    private static List<XdmNode> GetChildNodes(XdmNode node, INodeProvider? nodeProvider)
    {
        var children = new List<XdmNode>();
        var childIds = node switch
        {
            XdmElement elem => elem.Children,
            XdmDocument doc => doc.Children,
            _ => System.Collections.Immutable.ImmutableArray<NodeId>.Empty
        };
        foreach (var childId in childIds)
        {
            var child = nodeProvider?.GetNode(childId);
            if (child != null)
                children.Add(child);
        }
        return children;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// XQuery Update Facility — Physical Operators
//
// Status: LIVE. Update operators collect PUL entries during evaluation.
// The PendingUpdateList on QueryExecutionContext accumulates primitives,
// and TransformOperator applies them via deep-copy + PendingUpdateApplicator.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Insert node(s) into/before/after a target.
/// Collects an <see cref="Ast.InsertPrimitive"/> into the context's PUL.
/// </summary>
public sealed class InsertOperator : PhysicalOperator
{
    public required PhysicalOperator Source { get; init; }
    public required PhysicalOperator Target { get; init; }
    public required Ast.InsertPosition Position { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate source and target
        var sourceItems = new List<object?>();
        await foreach (var item in Source.ExecuteAsync(context))
            sourceItems.Add(item);

        object? targetNode = null;
        await foreach (var item in Target.ExecuteAsync(context))
        {
            targetNode = item;
            break;
        }

        if (targetNode == null)
            throw new XQueryRuntimeException("XUDY0027", "insert expression: target is empty.");

        var source = sourceItems.Count == 1 ? sourceItems[0] : sourceItems.ToArray();
        context.PendingUpdates.AddInsert(targetNode, source!, Position);

        yield break; // Update expressions produce no value, only PUL entries
    }
}

/// <summary>
/// Delete node(s).
/// Collects <see cref="Ast.DeletePrimitive"/> entries into the context's PUL.
/// </summary>
public sealed class DeleteOperator : PhysicalOperator
{
    public required PhysicalOperator Target { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await foreach (var item in Target.ExecuteAsync(context))
        {
            if (item != null)
                context.PendingUpdates.AddDelete(item);
        }

        yield break;
    }
}

/// <summary>
/// Replace a node with another.
/// Collects a <see cref="Ast.ReplaceNodePrimitive"/> into the context's PUL.
/// </summary>
public sealed class ReplaceNodeOperator : PhysicalOperator
{
    public required PhysicalOperator Target { get; init; }
    public required PhysicalOperator Replacement { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? targetNode = null;
        await foreach (var item in Target.ExecuteAsync(context))
        {
            targetNode = item;
            break;
        }

        if (targetNode == null)
            throw new XQueryRuntimeException("XUDY0027", "replace node expression: target is empty.");

        var replacementItems = new List<object?>();
        await foreach (var item in Replacement.ExecuteAsync(context))
            replacementItems.Add(item);

        var replacement = replacementItems.Count == 1 ? replacementItems[0] : replacementItems.ToArray();
        context.PendingUpdates.AddReplaceNode(targetNode, replacement!);

        yield break;
    }
}

/// <summary>
/// Replace a node's value.
/// Collects a <see cref="Ast.ReplaceValuePrimitive"/> into the context's PUL.
/// </summary>
public sealed class ReplaceValueOperator : PhysicalOperator
{
    public required PhysicalOperator Target { get; init; }
    public required PhysicalOperator Value { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? targetNode = null;
        await foreach (var item in Target.ExecuteAsync(context))
        {
            targetNode = item;
            break;
        }

        if (targetNode == null)
            throw new XQueryRuntimeException("XUDY0027", "replace value expression: target is empty.");

        object? value = null;
        await foreach (var item in Value.ExecuteAsync(context))
        {
            value = item;
            break;
        }

        context.PendingUpdates.AddReplaceValue(targetNode, value ?? "");

        yield break;
    }
}

/// <summary>
/// Rename a node.
/// Collects a <see cref="Ast.RenamePrimitive"/> into the context's PUL.
/// </summary>
public sealed class RenameOperator : PhysicalOperator
{
    public required PhysicalOperator Target { get; init; }
    public required PhysicalOperator NewName { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? targetNode = null;
        await foreach (var item in Target.ExecuteAsync(context))
        {
            targetNode = item;
            break;
        }

        if (targetNode == null)
            throw new XQueryRuntimeException("XUDY0027", "rename expression: target is empty.");

        object? nameValue = null;
        await foreach (var item in NewName.ExecuteAsync(context))
        {
            nameValue = item;
            break;
        }

        PhoenixmlDb.Core.QName qname;
        if (nameValue is PhoenixmlDb.Core.QName q)
            qname = q;
        else if (nameValue is string s)
            qname = new PhoenixmlDb.Core.QName(PhoenixmlDb.Core.NamespaceId.None, s);
        else
            throw new XQueryRuntimeException("XPTY0004",
                "rename expression: new name must be a QName or string.");

        context.PendingUpdates.AddRename(targetNode, qname);

        yield break;
    }
}

/// <summary>
/// Transform copy-modify-return (functional update).
/// Deep-copies source nodes, applies the modify clause's PUL to the copies,
/// then evaluates and returns the return clause.
/// </summary>
public sealed class TransformOperator : PhysicalOperator
{
    public required IReadOnlyList<TransformCopyBindingOperator> CopyBindings { get; init; }
    public required PhysicalOperator ModifyExpr { get; init; }
    public required PhysicalOperator ReturnExpr { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Save the outer PUL and create a fresh one for the modify clause
        var outerPul = context.PendingUpdates;
        var modifyPul = new Ast.PendingUpdateList();
        context.PendingUpdates = modifyPul;

        var store = new InMemoryUpdatableNodeStore();
        context.PushScope();
        context.PushNodeProvider(store);

        try
        {
            // Phase 1: Evaluate copy bindings — deep-copy each source node
            foreach (var binding in CopyBindings)
            {
                object? sourceValue = null;
                await foreach (var item in binding.Expression.ExecuteAsync(context))
                {
                    sourceValue = item;
                    break;
                }

                if (sourceValue is not PhoenixmlDb.Xdm.Nodes.XdmNode sourceNode)
                    throw new XQueryRuntimeException("XUTY0013",
                        "copy binding: source must be a node.");

                // Deep-copy with node resolution from the query context
                var copiedNode = store.DeepCopy(sourceNode, id =>
                {
                    // Try the store first (for nodes already copied), then fall back to context
                    return store.GetNode(id) ?? context.LoadNode(id);
                });

                context.BindVariable(binding.Variable, copiedNode);
            }

            // Phase 2: Evaluate modify clause — collects PUL entries against the copies
            await foreach (var _ in ModifyExpr.ExecuteAsync(context))
            {
                // Discard any values; we only want the PUL side effects
            }

            // Phase 3: Apply the collected PUL to the in-memory store
            if (modifyPul.HasUpdates)
            {
                PendingUpdateApplicator.Apply(modifyPul, store);
            }

            // Phase 3.5: Register all deep-copied (and potentially mutated) nodes in the
            // document store so the serializer can resolve them.
            if (context.NodeProvider is XdmDocumentStore docStore)
            {
                foreach (var node in store.AllNodes)
                    docStore.RegisterNode(node);
            }

            // Phase 4: Evaluate and yield the return clause
            await foreach (var item in ReturnExpr.ExecuteAsync(context))
            {
                yield return item;
            }
        }
        finally
        {
            context.PopNodeProvider();
            context.PopScope();
            context.PendingUpdates = outerPul;
        }
    }
}

/// <summary>
/// A copy binding in a transform expression.
/// </summary>
public sealed class TransformCopyBindingOperator
{
    public required PhoenixmlDb.Core.QName Variable { get; init; }
    public required PhysicalOperator Expression { get; init; }
}

/// <summary>
/// XQuery 3.1/4.0: String constructor ``[content `{expr}` more]``.
/// Evaluates to a single string by concatenating literal parts and stringified expression results.
/// </summary>
public sealed class StringConstructorOperator : PhysicalOperator
{
    public required IReadOnlyList<StringConstructorPartOp> Parts { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in Parts)
        {
            if (part.LiteralValue != null)
            {
                sb.Append(part.LiteralValue);
            }
            else if (part.ExpressionOperator != null)
            {
                // XQuery §3.11.2: items in a string constructor interpolation are
                // atomized and separated by single spaces (same as attribute content).
                bool firstItem = true;
                await foreach (var item in part.ExpressionOperator.ExecuteAsync(context))
                {
                    if (item != null)
                    {
                        if (!firstItem)
                            sb.Append(' ');
                        sb.Append(context.AtomizeWithNodes(item)?.ToString() ?? "");
                        firstItem = false;
                    }
                }
            }
        }
        yield return sb.ToString();
    }
}

/// <summary>
/// A part of a string constructor operator: either a literal string or an expression operator.
/// </summary>
public sealed class StringConstructorPartOp
{
    public string? LiteralValue { get; init; }
    public PhysicalOperator? ExpressionOperator { get; init; }
}

/// <summary>
/// XPath 4.0: record { name: value, ... } constructor.
/// Evaluates to a map (Dictionary) with string keys.
/// </summary>
public sealed class RecordConstructorOperator : PhysicalOperator
{
    public required IReadOnlyList<(string Name, PhysicalOperator Value)> Fields { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var result = new Dictionary<object, object?>();
        foreach (var (name, valueOp) in Fields)
        {
            object? value = null;
            await foreach (var item in valueOp.ExecuteAsync(context))
                value = item;
            result[name] = value;
        }
        yield return result;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// XQuery Full-Text — Physical Operator
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Evaluates a "contains text" expression using the Lucene.NET-powered full-text engine.
/// </summary>
public sealed class FtContainsOperator : PhysicalOperator
{
    public required PhysicalOperator Source { get; init; }
    public required Ast.FtSelectionNode Selection { get; init; }
    public Ast.FtMatchOptions? MatchOptions { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate the source expression to get the text to search
        object? sourceValue = null;
        await foreach (var item in Source.ExecuteAsync(context))
            sourceValue = item;

        var sourceText = context.AtomizeWithNodes(sourceValue)?.ToString() ?? "";
        var options = FullText.FullTextAnalysisOptions.FromFtMatchOptions(MatchOptions);

        var result = EvaluateSelection(sourceText, Selection, options);
        yield return result;
    }

    private static bool EvaluateSelection(string text, Ast.FtSelectionNode selection, FullText.FullTextAnalysisOptions options)
    {
        return selection switch
        {
            Ast.FtWordsNode words => EvaluateWords(text, words, options),
            Ast.FtAndNode and => and.Operands.All(op => EvaluateSelection(text, op, options)),
            Ast.FtOrNode or => or.Operands.Any(op => EvaluateSelection(text, op, options)),
            Ast.FtNotNode not => !EvaluateSelection(text, not.Operand, options),
            Ast.FtMildNotNode mn => EvaluateSelection(text, mn.Include, options) && !EvaluateSelection(text, mn.Exclude, options),
            Ast.FtSelectionWithFilters filtered => EvaluateWithFilters(text, filtered, options),
            _ => false
        };
    }

    private static bool EvaluateWords(string text, Ast.FtWordsNode words, FullText.FullTextAnalysisOptions options)
    {
        var searchText = words.Text ?? "";
        if (string.IsNullOrEmpty(searchText)) return true;
        return FullText.FullTextEngine.ContainsText(text, searchText, words.Mode, options);
    }

    private static bool EvaluateWithFilters(string text, Ast.FtSelectionWithFilters filtered, FullText.FullTextAnalysisOptions options)
    {
        // First check if the basic selection matches
        if (!EvaluateSelection(text, filtered.Selection, options))
            return false;

        // Apply position filters
        foreach (var filter in filtered.PositionFilters)
        {
            switch (filter.Type)
            {
                case Ast.FtPositionFilterType.Window:
                    // Check if matched terms are within N positions
                    if (filtered.Selection is Ast.FtWordsNode words)
                    {
                        var sourceTerms = FullText.FullTextEngine.Analyze(text, options);
                        var searchTerms = FullText.FullTextEngine.Analyze(words.Text ?? "", options);
                        if (!FullText.FullTextEngine.WithinWindow(sourceTerms, searchTerms, filter.Value))
                            return false;
                    }
                    break;

                case Ast.FtPositionFilterType.Ordered:
                    // Check terms appear in order — for phrase this is already guaranteed
                    break;

                case Ast.FtPositionFilterType.EntireContent:
                    // Check that the match covers the entire content
                    if (filtered.Selection is Ast.FtWordsNode entireWords)
                    {
                        var srcTerms = FullText.FullTextEngine.Analyze(text, options);
                        var srchTerms = FullText.FullTextEngine.Analyze(entireWords.Text ?? "", options);
                        if (srcTerms.Count != srchTerms.Count)
                            return false;
                    }
                    break;
            }
        }

        return true;
    }
}

/// <summary>
/// Thin arrow operator (->) for XPath 4.0.
/// Evaluates the expression, pushes its result as the context item,
/// then evaluates the function call in that context.
/// </summary>
public sealed class ThinArrowOperator : PhysicalOperator
{
    public required PhysicalOperator Expression { get; init; }
    public required PhysicalOperator FunctionCall { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? exprResult = null;
        await foreach (var item in Expression.ExecuteAsync(context))
            exprResult = item;

        // Push expression result as context item
        context.PushContextItem(exprResult, 1, 1);
        try
        {
            await foreach (var result in FunctionCall.ExecuteAsync(context))
                yield return result;
        }
        finally
        {
            context.PopContextItem();
        }
    }
}

/// <summary>
/// Validate expression operator: delegates to <see cref="ISchemaProvider.Validate"/>.
/// QueryEngine defaults to an XsdSchemaProvider; when a caller explicitly opts out by
/// passing <c>null</c>, validation can't run — we surface that as a runtime error.
/// </summary>
public sealed class ValidateOperator : PhysicalOperator
{
    public required PhysicalOperator ExpressionOperator { get; init; }
    public required ValidationMode Mode { get; init; }
    public Ast.XdmTypeName? TypeName { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var schemaProvider = context.SchemaProvider
            ?? throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQDY0027",
                "Validate expression requires a registered ISchemaProvider — " +
                "QueryEngine was constructed with schemaProvider: null. " +
                "Use the default XsdSchemaProvider or supply a custom implementation.");

        // Materialize the expression result
        XdmNode? node = null;
        await foreach (var item in ExpressionOperator.ExecuteAsync(context))
        {
            if (item is XdmNode n)
                node = n;
            else
                throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQDY0025",
                    "Validate expression requires a single document or element node.");
        }

        if (node is null)
            throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQDY0025",
                "Validate expression requires a single document or element node.");

        var validated = schemaProvider.Validate(node, Mode, TypeName?.NamespaceUri, TypeName?.LocalName);
        yield return validated;
    }
}
