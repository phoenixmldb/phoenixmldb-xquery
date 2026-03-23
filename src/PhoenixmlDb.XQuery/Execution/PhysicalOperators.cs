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
        if (item != null)
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
                    if (MatchesNodeTest(related) && seen.Add(related.Id))
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
                    if (MatchesNodeTest(related))
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

    private bool MatchesNodeTest(XdmNode node)
    {
        return NodeTest switch
        {
            NameTest nt => MatchesNameTest(node, nt, Axis),
            KindTest kt => MatchesKindTest(node, kt),
            _ => false
        };
    }

    internal static bool MatchesNameTest(XdmNode node, NameTest test, Axis axis)
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
            // ns:* — match specific namespace using resolved NamespaceId
            if (test.ResolvedNamespace.HasValue)
            {
                if (matchesAttribute)
                    return node is XdmAttribute a && a.Namespace == test.ResolvedNamespace.Value;
                if (matchesNamespace)
                    return false; // Namespace nodes don't have namespaces
                return node is XdmElement e && e.Namespace == test.ResolvedNamespace.Value;
            }
            return false;
        }

        // Named test: only matches principal node type
        var localName = (matchesAttribute, matchesNamespace) switch
        {
            (true, _) => node is XdmAttribute attr ? attr.LocalName : null,
            (_, true) => node is XdmNamespace ns ? ns.Prefix : null,
            _ => node switch
            {
                XdmElement elem => elem.LocalName,
                XdmProcessingInstruction pi => pi.Target,
                _ => null
            }
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

        // Check namespace using resolved NamespaceId (set during static analysis)
        if (test.ResolvedNamespace.HasValue)
            return nodeNs == test.ResolvedNamespace.Value;

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
        if (test.Name != null)
            return MatchesNameForKindTest(node, test.Name);

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
                if (MatchesNodeTest(related))
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
        object? result = null;
        await foreach (var item in predOp.ExecuteAsync(context))
        {
            result = item;
            break;
        }
        return result;
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

    private bool MatchesNodeTest(XdmNode node) => NodeTest switch
    {
        NameTest nt => AxisNavigationOperator.MatchesNameTest(node, nt, Axis),
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
        // Execute the predicate operator and get the result
        object? result = null;
        await foreach (var item in PredicateOperator.ExecuteAsync(context))
        {
            result = item;
            break; // Take first result for predicate evaluation
        }
        return result;
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

        // Order by: collect all tuples from preceding clauses, sort, then yield
        if (clause is OrderByClauseOperator orderBy)
        {
            // At this point, the caller has already been yielding tuples one at a time.
            // We need to be called differently. Instead, we handle this by collecting
            // in the parent's recursion. Skip to the next clause - sorting is done
            // by the parent when it detects we're an order-by.
            await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
            {
                yield return restTuple;
            }
            yield break;
        }

        // Collect all tuples from this clause
        var allBindings = new List<Dictionary<QName, object?>>();
        await foreach (var bindings in clause.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            allBindings.Add(bindings);
            context.CheckMaterializationLimit(allBindings.Count);
        }

        // Check if next clause is order by - if so, sort before continuing
        if (index + 1 < Clauses.Count && Clauses[index + 1] is OrderByClauseOperator nextOrderBy)
        {
            allBindings = await SortTuplesAsync(allBindings, nextOrderBy, context);

            foreach (var bindings in allBindings)
            {
                context.PushScope();
                foreach (var (name, value) in bindings)
                    context.BindVariable(name, value);

                try
                {
                    // Skip the order by clause (index + 2)
                    await foreach (var restTuple in ExecuteClausesAsync(context, index + 2))
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
            yield break;
        }

        // Normal clause processing
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
                await foreach (var item in spec.KeyOperator.ExecuteAsync(context))
                {
                    key = item;
                    break;
                }
                keys.Add(key);
            }

            context.PopScope();
            keyed.Add((tuple, keys));
        }

        keyed.Sort((a, b) =>
        {
            for (int i = 0; i < orderBy.OrderSpecs.Count; i++)
            {
                var spec = orderBy.OrderSpecs[i];
                var ka = i < a.Keys.Count ? a.Keys[i] : null;
                var kb = i < b.Keys.Count ? b.Keys[i] : null;

                var cmp = CompareValues(ka, kb, spec.EmptyOrder);
                if (spec.Direction == Ast.OrderDirection.Descending)
                    cmp = -cmp;

                if (cmp != 0)
                    return cmp;
            }
            return 0;
        });

        return keyed.Select(k => k.Tuple).ToList();
    }

    private static int CompareValues(object? a, object? b, Ast.EmptyOrder emptyOrder)
    {
        if (a == null && b == null)
            return 0;
        if (a == null)
            return emptyOrder == Ast.EmptyOrder.Least ? -1 : 1;
        if (b == null)
            return emptyOrder == Ast.EmptyOrder.Least ? 1 : -1;

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
                    item = QueryExecutionContext.Atomize(item);
                    item = TypeCastHelper.CastValue(item, binding.TypeDeclaration.ItemType);
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
            if (binding.TypeDeclaration != null)
            {
                item = QueryExecutionContext.Atomize(item);
                item = TypeCastHelper.CastValue(item, binding.TypeDeclaration.ItemType);
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
                value = QueryExecutionContext.Atomize(value);
                if (value is object?[] arr)
                    value = arr.Select(v => TypeCastHelper.CastValue(v, binding.TypeDeclaration.ItemType)).ToArray();
                else
                    value = TypeCastHelper.CastValue(value, binding.TypeDeclaration.ItemType);
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
        object? result = null;
        await foreach (var item in ConditionOperator.ExecuteAsync(context))
        {
            result = item;
            break; // Take first item
        }

        if (QueryExecutionContext.EffectiveBooleanValue(result))
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
    public string? Collation { get; init; }
}

/// <summary>
/// Count clause operator.
/// </summary>
public sealed class CountClauseOperator : FlworClauseOperator
{
    public required QName Variable { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        // Count is maintained at FLWOR level
        await Task.CompletedTask;
        yield return new Dictionary<QName, object?>();
    }
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

        // XDM arrays and maps are items, not sequences — yield as-is based on return type
        if (result is IDictionary<object, object?>
            || (result is List<object?> && function.ReturnType?.ItemType is Ast.ItemType.Array or Ast.ItemType.Map))
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
                    rightItems.Add(item);
            }
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null && rightItems.Contains(item))
                    yield return item;
            }
            yield break;
        }

        if (Operator is BinaryOperator.Except)
        {
            var rightItems = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                    rightItems.Add(item);
            }
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null && !rightItems.Contains(item))
                    yield return item;
            }
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
                leftItems.Add(item);

            var rightItemsList = new List<object?>();
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
                rightItemsList.Add(item);

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
                bool anyBoolLeft = leftItems.Any(i => QueryExecutionContext.Atomize(i) is bool);
                bool anyBoolRight = rightItemsList.Any(i => QueryExecutionContext.Atomize(i) is bool);
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
            if (leftCount == 0)
                leftValue = item;
            leftCount++;
            if (leftCount > 1)
                break;
        }

        object? rightValue = null;
        int rightCount = 0;
        await foreach (var item in Right.ExecuteAsync(context))
        {
            if (rightCount == 0)
                rightValue = item;
            rightCount++;
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

        // XPath 1.0 backwards-compatible: arithmetic always uses doubles,
        // and empty sequences become NaN.
        if (context.BackwardsCompatible && Operator is
            BinaryOperator.Add or BinaryOperator.Subtract or
            BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo)
        {
            var ld = CoerceToDouble(QueryExecutionContext.Atomize(leftValue));
            var rd = CoerceToDouble(QueryExecutionContext.Atomize(rightValue));
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
            var leftNode = left as Xdm.Nodes.XdmNode;
            var rightNode = right as Xdm.Nodes.XdmNode;
            if (leftNode is null || rightNode is null)
                return null; // empty sequence if either operand is not a node
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
                return (ua.Value.Length > 0, right); // Cast to boolean
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
                return (left, ua.Value.Length > 0); // Cast to boolean
            // Cast untyped to match date/time/duration/QName types (FORG0001 on failure)
            var leftItemType = GetItemTypeForValue(left);
            if (leftItemType != null)
                return (left, CastUntypedToType(ua.Value, leftItemType.Value));
            return (left, ua.Value); // Cast to string for string/other comparisons
        }
        return (left, right); // Neither is untyped — no conversion needed
    }

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

        // If either is double/float, promote both to double
        if (left is double or float || right is double or float)
            return (left is BigInteger lbi ? (double)lbi : Convert.ToDouble(left),
                    right is BigInteger rbi ? (double)rbi : Convert.ToDouble(right));
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

        // Date/time + duration arithmetic (new wrapper types)
        if (left is Xdm.XsDate xld && right is Xdm.YearMonthDuration ymd)
            return new Xdm.XsDate(xld.Date.AddMonths(ymd.TotalMonths), xld.Timezone);
        if (left is Xdm.YearMonthDuration ymd2 && right is Xdm.XsDate xrd)
            return new Xdm.XsDate(xrd.Date.AddMonths(ymd2.TotalMonths), xrd.Timezone);
        if (left is Xdm.XsDate xld2 && right is TimeSpan ts)
            return new Xdm.XsDate(xld2.Date.AddDays((int)ts.TotalDays), xld2.Timezone);
        if (left is TimeSpan ts2 && right is Xdm.XsDate xrd2)
            return new Xdm.XsDate(xrd2.Date.AddDays((int)ts2.TotalDays), xrd2.Timezone);
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
                (double a, double b) => a + b,
                (decimal a, decimal b) => a + b,
                _ => Convert.ToDouble(l) + Convert.ToDouble(r)
            };
        }
        return ToDouble(left) + ToDouble(right);
    }

    private static object? Subtract(object? left, object? right)
    {
        // Empty sequence in arithmetic → NaN (supports BCM and format-number)
        if (left is null)
            left = double.NaN;
        if (right is null)
            right = double.NaN;

        // Date/time - duration arithmetic (new wrapper types)
        if (left is Xdm.XsDate xld && right is Xdm.YearMonthDuration ymd)
            return new Xdm.XsDate(xld.Date.AddMonths(-ymd.TotalMonths), xld.Timezone);
        if (left is Xdm.XsDate xld2 && right is TimeSpan ts)
            return new Xdm.XsDate(xld2.Date.AddDays(-(int)ts.TotalDays), xld2.Timezone);
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
            return xlt2.Time.ToTimeSpan() - xrt.Time.ToTimeSpan();
        if (left is Xdm.XsDate xld3 && right is Xdm.XsDate xrd)
            return xld3.Date.ToDateTime(TimeOnly.MinValue) - xrd.Date.ToDateTime(TimeOnly.MinValue);
        if (left is Xdm.XsDateTime xldt3 && right is Xdm.XsDateTime xrdt)
            return xldt3.Value - xrdt.Value;
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
            return ymd * ToDouble(right);
        if (IsNumeric(left) && right is Xdm.YearMonthDuration ymd2)
            return ymd2 * ToDouble(left);
        if (left is TimeSpan ts && IsNumeric(right))
            return TimeSpan.FromTicks((long)(ts.Ticks * ToDouble(right)));
        if (IsNumeric(left) && right is TimeSpan ts2)
            return TimeSpan.FromTicks((long)(ts2.Ticks * ToDouble(left)));

        if (IsNumeric(left) && IsNumeric(right))
        {
            var (l, r) = PromoteNumeric(left, right);
            return (l, r) switch
            {
                (long a, long b) => LongMultiplyOrPromote(a, b),
                (BigInteger a, BigInteger b) => a * b,
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

        // Duration / number
        if (left is Xdm.YearMonthDuration ymd && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (d == 0)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
            return new Xdm.YearMonthDuration((int)Math.Round(ymd.TotalMonths / d));
        }
        if (left is TimeSpan ts && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (d == 0)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
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
        if ((left is double ld2 && (double.IsNaN(ld2) || double.IsInfinity(ld2))) ||
            (right is double rd2 && double.IsNaN(rd2)))
            throw new XQueryRuntimeException("FOAR0002", "Invalid argument for integer division");
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
        var l = Convert.ToInt64(left is string sl ? ToDouble(sl) : left);
        var r = Convert.ToInt64(right is string sr ? ToDouble(sr) : right);
        if (r == 0)
            throw new XQueryRuntimeException("FOAR0001", "Division by zero");
        return l / r;
    }

    private static object? Modulo(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;

        if (IsNumeric(left) && IsNumeric(right))
        {
            if (left is double or float || right is double or float)
            {
                var ld = left is BigInteger lbi ? (double)lbi : Convert.ToDouble(left);
                var rd = right is BigInteger rbi ? (double)rbi : Convert.ToDouble(right);
                if (rd == 0)
                    throw new XQueryRuntimeException("FOAR0001", "Division by zero");
                return ld % rd;
            }
            if (left is decimal || right is decimal)
            {
                try
                {
                    var ld = left is BigInteger lbi2 ? (decimal)lbi2 : Convert.ToDecimal(left);
                    var rd = right is BigInteger rbi2 ? (decimal)rbi2 : Convert.ToDecimal(right);
                    if (rd == 0)
                        throw new XQueryRuntimeException("FOAR0001", "Division by zero");
                    return ld % rd;
                }
                catch (OverflowException)
                {
                    // BigInteger too large for decimal — fall through to double
                    var ldd = left is BigInteger lbi3 ? (double)lbi3 : Convert.ToDouble(left);
                    var rdd = right is BigInteger rbi3 ? (double)rbi3 : Convert.ToDouble(right);
                    if (rdd == 0)
                        throw new XQueryRuntimeException("FOAR0001", "Division by zero");
                    return ldd % rdd;
                }
            }
            if (left is BigInteger || right is BigInteger)
            {
                var lb = ToBigInteger(left);
                var rb = ToBigInteger(right);
                if (rb.IsZero)
                    throw new XQueryRuntimeException("FOAR0001", "Division by zero");
                var result = BigInteger.Remainder(lb, rb);
                if (result >= long.MinValue && result <= long.MaxValue)
                    return (long)result;
                return result;
            }
        }
        var ll = (long)ToDouble(left);
        var rl = (long)ToDouble(right);
        if (rl == 0)
            throw new XQueryRuntimeException("FOAR0001", "Division by zero");
        return ll % rl;
    }

    private static bool IsNumeric(object? v) =>
        v is int or long or double or float or decimal or BigInteger;

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
            // If either is double or float, compare as double
            if (left is double or float || right is double or float)
            {
                var ld = ToDouble(left);
                var rd = ToDouble(right);
                // NaN != NaN per XQuery/IEEE 754
                if (double.IsNaN(ld) || double.IsNaN(rd))
                    return false;
                return ld == rd;
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
        if (left is QName lq && right is QName rq)
        {
            if (lq.LocalName != rq.LocalName)
                return false;
            // When both have resolved namespace URIs, compare by URI string
            // (handles different NamespaceId interning from different creation pathways)
            var lUri = lq.ResolvedNamespace;
            var rUri = rq.ResolvedNamespace;
            if (lUri != null && rUri != null)
                return lUri == rUri;
            return lq.Namespace == rq.Namespace;
        }

        // Date/time comparison — normalize to UTC before comparing
        if (left is Xdm.XsDateTime ldt && right is Xdm.XsDateTime rdt)
            return ldt.CompareTo(rdt) == 0;
        if (left is Xdm.XsDate ld2 && right is Xdm.XsDate rd2)
            return ld2.CompareTo(rd2) == 0;
        if (left is Xdm.XsTime lt && right is Xdm.XsTime rt)
            return lt.CompareTo(rt) == 0;

        // Duration comparison
        if (left is Xdm.XsDuration lxd && right is Xdm.XsDuration rxd)
            return lxd == rxd;
        if (left is Xdm.YearMonthDuration lym && right is Xdm.YearMonthDuration rym)
            return lym.TotalMonths == rym.TotalMonths;
        if (left is TimeSpan lts && right is TimeSpan rts)
            return lts == rts;

        // Binary comparison — compare underlying byte arrays
        if (left is Xdm.XdmValue lv && right is Xdm.XdmValue rv
            && lv.Type == rv.Type
            && lv.RawValue is byte[] lBytes && rv.RawValue is byte[] rBytes)
            return lBytes.AsSpan().SequenceEqual(rBytes);

        // String comparison using XPath canonical string representations
        return string.Equals(
            Functions.ConcatFunction.XQueryStringValue(left),
            Functions.ConcatFunction.XQueryStringValue(right),
            stringComparison);
    }

    private static int ValueCompare(object? left, object? right, StringComparison stringComparison = StringComparison.Ordinal)
    {
        // Atomize XDM nodes before comparison
        left = QueryExecutionContext.Atomize(left);
        right = QueryExecutionContext.Atomize(right);

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

        // Duration comparison — only yearMonthDuration and dayTimeDuration support ordering
        if (left is Xdm.YearMonthDuration lym && right is Xdm.YearMonthDuration rym)
            return lym.CompareTo(rym);
        if (left is TimeSpan lts && right is TimeSpan rts)
            return lts.CompareTo(rts);
        // xs:duration only supports eq/ne, not ordering
        if (left is Xdm.XsDuration || right is Xdm.XsDuration)
            throw new Functions.XQueryException("XPTY0004",
                "Values of type xs:duration are not ordered — cannot use lt, gt, le, ge");

        // Gregorian date component types only support eq/ne, not ordering
        if (left is Xdm.XsGYear or Xdm.XsGMonth or Xdm.XsGDay or Xdm.XsGMonthDay or Xdm.XsGYearMonth ||
            right is Xdm.XsGYear or Xdm.XsGMonth or Xdm.XsGDay or Xdm.XsGMonthDay or Xdm.XsGYearMonth)
            throw new Functions.XQueryException("XPTY0004",
                "Gregorian date component types are not ordered — cannot use lt, gt, le, ge");

        // String comparison using XPath canonical string representations
        return string.Compare(
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
        }
        else
        {
            value = null;
            await foreach (var item in Operand.ExecuteAsync(context))
            {
                value = item;
                break;
            }
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
        if (value is string s)
            return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        if (value is Xdm.XsUntypedAtomic u)
            return double.TryParse(u.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d2) ? d2 : double.NaN;
        return Convert.ToDouble(value);
    }

    private static object? Negate(object? value, bool backwardsCompatible = false)
    {
        value = QueryExecutionContext.Atomize(value);
        if (value is string s)
            return -(double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN);
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

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeProvider as XdmDocumentStore;
        if (store == null)
        {
            // Fallback: serialize as XML string when no store is available
            yield return SerializeAsString(context);
            yield break;
        }

        var nsId = Name.ResolvedNamespace != null
            ? store.ResolveNamespace(Name.ResolvedNamespace)
            : Name.Namespace;

        // Evaluate attributes
        var attrIds = new List<NodeId>();
        var elemId = store.AllocateNodeId();
        var constructedDocId = new DocumentId(0);

        foreach (var attrOp in AttributeOperators)
        {
            await foreach (var attrResult in attrOp.ExecuteAsync(context))
            {
                if (attrResult is XdmAttribute attr)
                {
                    // Deep-copy attribute into constructed tree with new parent
                    var newAttrId = store.AllocateNodeId();
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

        // Evaluate content
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
                textNode.Parent = elemId;
                store.RegisterNode(textNode);
                childIds.Add(textId);
                pendingText.Clear();
            }
        }

        foreach (var contentOp in ContentOperators)
        {
            await foreach (var contentResult in contentOp.ExecuteAsync(context))
            {
                if (contentResult is XdmElement childElem)
                {
                    FlushPendingText();
                    // Deep-copy the element into the constructed tree
                    var copyId = DeepCopyNode(childElem, store, constructedDocId, elemId);
                    childIds.Add(copyId);
                }
                else if (contentResult is XdmDocument doc)
                {
                    // Document node: add its children as content
                    FlushPendingText();
                    foreach (var docChildId in doc.Children)
                    {
                        var docChild = store.GetNode(docChildId);
                        if (docChild != null)
                        {
                            var copyId = DeepCopyNode(docChild, store, constructedDocId, elemId);
                            childIds.Add(copyId);
                        }
                    }
                }
                else if (contentResult is XdmText text)
                {
                    // Merge adjacent text
                    pendingText ??= new StringBuilder();
                    pendingText.Append(text.Value);
                }
                else if (contentResult is XdmComment || contentResult is XdmProcessingInstruction)
                {
                    FlushPendingText();
                    var copyId = DeepCopyNode((XdmNode)contentResult, store, constructedDocId, elemId);
                    childIds.Add(copyId);
                }
                else if (contentResult is XdmAttribute)
                {
                    // Attributes appearing in content are added as attributes per XQuery spec
                    var contentAttr = (XdmAttribute)contentResult;
                    var newAttrId = store.AllocateNodeId();
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
                else if (contentResult != null)
                {
                    // Atomic values become text nodes; merge adjacent text
                    pendingText ??= new StringBuilder();
                    var atomicText = QueryExecutionContext.Atomize(contentResult)?.ToString() ?? "";
                    if (pendingText.Length > 0 && atomicText.Length > 0)
                        pendingText.Append(' ');
                    pendingText.Append(atomicText);
                }
            }
        }

        FlushPendingText();

        // Build namespace declarations
        var nsDecls = new List<NamespaceBinding>();
        if (nsId != NamespaceId.None)
        {
            nsDecls.Add(new NamespaceBinding(Name.Prefix ?? "", nsId));
        }

        var elem = new XdmElement
        {
            Id = elemId,
            Document = constructedDocId,
            Namespace = nsId,
            LocalName = Name.LocalName,
            Prefix = Name.Prefix,
            Attributes = attrIds,
            Children = childIds,
            NamespaceDeclarations = nsDecls
        };
        elem.Parent = null;
        store.RegisterNode(elem);

        yield return elem;
    }

    private async Task<string> SerializeAsString(QueryExecutionContext context)
    {
        var sb = new StringBuilder();
        var name = Name.Prefix != null ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        sb.Append('<').Append(name);

        // Serialize attributes
        foreach (var attrOp in AttributeOperators)
        {
            await foreach (var attrResult in attrOp.ExecuteAsync(context))
            {
                if (attrResult is XdmAttribute attr)
                {
                    var attrName = attr.Prefix != null ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName;
                    sb.Append(' ').Append(attrName).Append("=\"").Append(EscapeXmlAttribute(attr.Value)).Append('"');
                }
            }
        }

        sb.Append('>');

        // Serialize content
        foreach (var contentOp in ContentOperators)
        {
            await foreach (var contentResult in contentOp.ExecuteAsync(context))
            {
                if (contentResult is XdmNode node)
                    sb.Append(node.StringValue);
                else if (contentResult != null)
                    sb.Append(QueryExecutionContext.Atomize(contentResult)?.ToString() ?? "");
            }
        }

        sb.Append("</").Append(name).Append('>');
        return sb.ToString();
    }

    internal static NodeId DeepCopyNode(XdmNode source, XdmDocumentStore store, DocumentId docId, NodeId? parentId)
    {
        var newId = store.AllocateNodeId();

        switch (source)
        {
            case XdmElement elem:
            {
                var newAttrs = new List<NodeId>();
                var newChildren = new List<NodeId>();

                var newElem = new XdmElement
                {
                    Id = newId,
                    Document = docId,
                    Namespace = elem.Namespace,
                    LocalName = elem.LocalName,
                    Prefix = elem.Prefix,
                    Attributes = newAttrs,
                    Children = newChildren,
                    NamespaceDeclarations = elem.NamespaceDeclarations
                };
                newElem.Parent = parentId;
                store.RegisterNode(newElem);

                foreach (var attrId in elem.Attributes)
                {
                    if (store.GetNode(attrId) is XdmAttribute attr)
                    {
                        var newAttrId = store.AllocateNodeId();
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
                        var childCopyId = DeepCopyNode(child, store, docId, newId);
                        newChildren.Add(childCopyId);
                    }
                }

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
                var atomized = QueryExecutionContext.Atomize(item);
                sb.Append(atomized?.ToString() ?? "");
            }
        }

        if (store != null)
        {
            var nsId = Name.ResolvedNamespace != null
                ? store.ResolveNamespace(Name.ResolvedNamespace)
                : Name.Namespace;

            var attrId = store.AllocateNodeId();
            var attr = new XdmAttribute
            {
                Id = attrId,
                Document = new DocumentId(0),
                Namespace = nsId,
                LocalName = Name.LocalName,
                Prefix = Name.Prefix,
                Value = sb.ToString()
            };
            store.RegisterNode(attr);
            yield return attr;
        }
        else
        {
            // Fallback: return a synthetic attribute node
            var attr = new XdmAttribute
            {
                Id = new NodeId(0),
                Document = new DocumentId(0),
                Namespace = NamespaceId.None,
                LocalName = Name.LocalName,
                Prefix = Name.Prefix,
                Value = sb.ToString()
            };
            yield return attr;
        }
    }
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
        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                var atomized = QueryExecutionContext.Atomize(item);
                sb.Append(atomized?.ToString() ?? "");
            }
        }

        // Per XQuery spec: if the content is empty string, no text node is created
        if (sb.Length == 0)
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
                var atomized = QueryExecutionContext.Atomize(item);
                sb.Append(atomized?.ToString() ?? "");
            }
        }

        var value = sb.ToString();
        // Per XQuery spec: comment must not end with '-' or contain '--'
        if (value.EndsWith('-'))
            value += " ";
        value = value.Replace("--", "- -");

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
            await foreach (var item in TargetOperator.ExecuteAsync(context))
            {
                target = QueryExecutionContext.Atomize(item)?.ToString() ?? "";
                break;
            }
        }
        target ??= "";

        // Evaluate content
        var sb = new StringBuilder();
        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                var atomized = QueryExecutionContext.Atomize(item);
                sb.Append(atomized?.ToString() ?? "");
            }
        }

        var value = sb.ToString();
        // Per XQuery spec: PI content must not contain '?>'
        value = value.Replace("?>", "? >");

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

        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item is XdmElement childElem)
            {
                FlushPendingText();
                var copyId = ElementConstructorOperator.DeepCopyNode(childElem, store, constructedDocId, docId);
                childIds.Add(copyId);
                docElement ??= copyId;
            }
            else if (item is XdmComment || item is XdmProcessingInstruction)
            {
                FlushPendingText();
                var copyId = ElementConstructorOperator.DeepCopyNode((XdmNode)item, store, constructedDocId, docId);
                childIds.Add(copyId);
            }
            else if (item is XdmText text)
            {
                pendingText ??= new StringBuilder();
                pendingText.Append(text.Value);
            }
            else if (item is XdmDocument nestedDoc)
            {
                // Unwrap nested document
                foreach (var nestedChildId in nestedDoc.Children)
                {
                    var nestedChild = store.GetNode(nestedChildId);
                    if (nestedChild != null)
                    {
                        FlushPendingText();
                        var copyId = ElementConstructorOperator.DeepCopyNode(nestedChild, store, constructedDocId, docId);
                        childIds.Add(copyId);
                        if (nestedChild is XdmElement && docElement == null)
                            docElement = copyId;
                    }
                }
            }
            else if (item != null)
            {
                pendingText ??= new StringBuilder();
                var atomicVal = QueryExecutionContext.Atomize(item)?.ToString() ?? "";
                if (pendingText.Length > 0 && atomicVal.Length > 0)
                    pendingText.Append(' ');
                pendingText.Append(atomicVal);
            }
        }

        FlushPendingText();

        var doc = new XdmDocument
        {
            Id = docId,
            Document = constructedDocId,
            Children = childIds,
            DocumentElement = docElement
        };
        doc.Parent = null;
        store.RegisterNode(doc);

        yield return doc;
    }
}

/// <summary>
/// Computed element constructor operator — element { nameExpr } { contentExpr }.
/// The element name is computed at runtime from an expression.
/// </summary>
public sealed class ComputedElementConstructorOperator : PhysicalOperator
{
    public required PhysicalOperator NameOperator { get; init; }
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate name
        string localName = "";
        string? prefix = null;

        await foreach (var nameResult in NameOperator.ExecuteAsync(context))
        {
            var nameVal = QueryExecutionContext.Atomize(nameResult)?.ToString() ?? "";
            if (nameVal.Contains(':'))
            {
                var parts = nameVal.Split(':', 2);
                prefix = parts[0];
                localName = parts[1];
            }
            else
            {
                localName = nameVal;
            }
            break;
        }

        // Delegate to ElementConstructorOperator using the resolved name
        var name = new QName(NamespaceId.None, localName, prefix);
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

        await foreach (var nameResult in NameOperator.ExecuteAsync(context))
        {
            var nameVal = QueryExecutionContext.Atomize(nameResult)?.ToString() ?? "";
            if (nameVal.Contains(':'))
            {
                var parts = nameVal.Split(':', 2);
                prefix = parts[0];
                localName = parts[1];
            }
            else
            {
                localName = nameVal;
            }
            break;
        }

        var name = new QName(NamespaceId.None, localName, prefix);
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

        startVal = QueryExecutionContext.Atomize(startVal);
        endVal = QueryExecutionContext.Atomize(endVal);
        var s = startVal is string ss ? (long)double.Parse(ss, System.Globalization.CultureInfo.InvariantCulture)
            : startVal is BigInteger sbi ? (long)sbi : Convert.ToInt64(startVal);
        var e = endVal is string es ? (long)double.Parse(es, System.Globalization.CultureInfo.InvariantCulture)
            : endVal is BigInteger ebi ? (long)ebi : Convert.ToInt64(endVal);
        for (var i = s; i <= e; i++)
        {
            if ((i - s) % 1024 == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            yield return i;
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

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? value = null;
        bool hasValue = false;
        await foreach (var item in Operand.ExecuteAsync(context))
        {
            value = item;
            hasValue = true;
            break;
        }

        if (!hasValue && TargetType.Occurrence == Occurrence.ZeroOrOne)
        {
            yield break;
        }

        if (!hasValue)
            throw new XQueryRuntimeException("XPTY0004", "Empty sequence cannot be cast to non-optional type");

        yield return TypeCastHelper.CastValue(value, TargetType.ItemType);
    }
}

/// <summary>
/// Castable expression: expr castable as type → boolean.
/// </summary>
public sealed class CastableOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? value = null;
        bool hasValue = false;
        await foreach (var item in Operand.ExecuteAsync(context))
        {
            value = item;
            hasValue = true;
            break;
        }

        if (!hasValue)
        {
            yield return TargetType.Occurrence == Occurrence.ZeroOrOne;
            yield break;
        }

        bool castable;
        try
        {
            TypeCastHelper.CastValue(value, TargetType.ItemType);
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

        yield return TypeCastHelper.MatchesType(items, TargetType);
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
                if (item != null && !TypeCastHelper.MatchesSequenceItemType(item, TargetType))
                    throw new XQueryRuntimeException("XPDY0050",
                        $"An item in the sequence does not match the required type {TargetType}: got {item.GetType().Name}");
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
            object? satVal = null;
            await foreach (var item in Satisfies.ExecuteAsync(context))
            { satVal = item; break; }
            return QueryExecutionContext.EffectiveBooleanValue(satVal);
        }

        var binding = Bindings[index];
        var items = new List<object?>();
        await foreach (var item in binding.InputOperator.ExecuteAsync(context))
            items.Add(item);

        if (Quantifier == Quantifier.Some)
        {
            foreach (var item in items)
            {
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
}

/// <summary>
/// Try-catch expression.
/// </summary>
public sealed class TryCatchOperator : PhysicalOperator
{
    public required PhysicalOperator TryOperator { get; init; }
    public required IReadOnlyList<CatchClauseOperator> CatchClauses { get; init; }

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
            // Find matching catch clause and collect its results
            results = await ExecuteCatchAsync(ex, context);
        }

        foreach (var item in results)
            yield return item;
    }

    private async Task<List<object?>> ExecuteCatchAsync(XQueryRuntimeException ex, QueryExecutionContext context)
    {
        foreach (var clause in CatchClauses)
        {
            if (clause.Matches(ex.ErrorCode))
            {
                var catchResults = new List<object?>();
                context.PushScope();
                context.BindVariable(new QName(default, "code", "err"), ex.ErrorCode);
                context.BindVariable(new QName(default, "description", "err"), ex.Message);
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

    public bool Matches(string errorCode)
    {
        foreach (var test in ErrorCodes)
        {
            if (test.LocalName == "*")
                return true;
            if (test.LocalName == errorCode)
                return true;
        }
        return false;
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

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        if (!RequiresPositionalAccess)
        {
            var position = 0;
            await foreach (var item in Left.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
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
        await foreach (var item in Operand.ExecuteAsync(context))
        { operandVal = item; break; }

        foreach (var @case in Cases)
        {
            foreach (var valueOp in @case.Values)
            {
                object? caseVal = null;
                await foreach (var item in valueOp.ExecuteAsync(context))
                { caseVal = item; break; }

                if (TypeCastHelper.DeepEquals(operandVal, caseVal))
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
        var map = new Dictionary<object, object?>();
        foreach (var entry in Entries)
        {
            object? key = null;
            await foreach (var item in entry.Key.ExecuteAsync(context))
            { key = item; break; }
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
/// </summary>
public sealed class LookupOperator : PhysicalOperator
{
    public required PhysicalOperator Base { get; init; }
    public PhysicalOperator? Key { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? baseVal = null;
        await foreach (var item in Base.ExecuteAsync(context))
        { baseVal = item; break; }

        if (baseVal == null)
            yield break;

        if (Key == null)
        {
            // Wildcard lookup ?* — return all values
            if (baseVal is Dictionary<object, object?> map)
            {
                foreach (var value in map.Values)
                    yield return value;
            }
            else if (baseVal is IList<object?> arrList)
            {
                foreach (var item in arrList)
                {
                    // Array members may be multi-item sequences stored as object?[]
                    if (item is object?[] memberSeq)
                        foreach (var mv in memberSeq)
                            yield return mv;
                    else
                        yield return item;
                }
            }
            yield break;
        }

        object? keyVal = null;
        await foreach (var item in Key.ExecuteAsync(context))
        { keyVal = item; break; }

        if (baseVal is Dictionary<object, object?> m)
        {
            if (keyVal != null && m.TryGetValue(keyVal, out var val))
                yield return val;
        }
        else if (baseVal is IList<object?> a)
        {
            var index = Convert.ToInt32(keyVal) - 1; // XQuery arrays are 1-based
            if (index >= 0 && index < a.Count)
            {
                var member = a[index];
                if (member is object?[] memberSeq)
                    foreach (var mv in memberSeq)
                        yield return mv;
                else
                    yield return member;
            }
        }
    }
}

/// <summary>
/// Unary lookup operator (?key) — looks up a key on the context item.
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
            if (contextItem is Dictionary<object, object?> map)
            {
                foreach (var value in map.Values)
                    yield return value;
            }
            else if (contextItem is IList<object?> arr)
            {
                foreach (var item in arr)
                {
                    if (item is object?[] memberSeq)
                        foreach (var mv in memberSeq)
                            yield return mv;
                    else
                        yield return item;
                }
            }
            yield break;
        }

        object? keyVal = null;
        await foreach (var item in Key.ExecuteAsync(context))
        { keyVal = item; break; }

        if (contextItem is Dictionary<object, object?> m)
        {
            if (keyVal != null && m.TryGetValue(keyVal, out var val))
                yield return val;
        }
        else if (contextItem is IList<object?> a)
        {
            var index = Convert.ToInt32(keyVal) - 1; // XQuery arrays are 1-based
            if (index >= 0 && index < a.Count)
            {
                var member = a[index];
                if (member is object?[] memberSeq)
                    foreach (var mv in memberSeq)
                        yield return mv;
                else
                    yield return member;
            }
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
        // Wrap variadic functions so function-arity() returns the requested arity
        if (func.IsVariadic && func.Arity != Arity)
            yield return new VariadicFunctionRefItem(func, Arity);
        else
            yield return func;
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

    public override bool IsVariadic => _inner.IsVariadic;
    public override int MaxArity => _inner.MaxArity;

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

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        // Create a closure that captures the current scope
        yield return new InlineFunctionItem(Parameters, Body, context);
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
    private ExecutionPlan? _cachedPlan;

    public InlineFunctionItem(
        IReadOnlyList<FunctionParameter> parameters,
        XQueryExpression body,
        QueryExecutionContext context)
    {
        _parameters = parameters;
        _body = body;
        _capturedContext = context;
        // Capture a snapshot of all in-scope variables to support closures.
        // Without this, variables from enclosing scopes (e.g., XSLT function params)
        // would be lost when the closure is invoked after the enclosing scope exits.
        _closureVariables = context.CaptureVariables();
    }

    public override QName Name => new(default, "anonymous");
    public override bool IsAnonymous => true;
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
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
                // Validate argument type against declared parameter type (XPTY0004)
                if (paramType != null && arg != null
                    && paramType.ItemType != Ast.ItemType.Item
                    && paramType.ItemType != Ast.ItemType.AnyAtomicType)
                {
                    // Atomize nodes to get their typed value
                    var checkVal = arg;
                    if (checkVal is XdmNode)
                        checkVal = QueryExecutionContext.Atomize(checkVal);
                    // UntypedAtomic can be cast to any atomic type — skip check
                    if (checkVal is not XsUntypedAtomic
                        && checkVal != null
                        && !TypeCastHelper.MatchesItemType(checkVal, paramType.ItemType)
                        && !IsInlineParamPromotion(checkVal, paramType.ItemType))
                    {
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Inline function parameter ${_parameters[i].Name.LocalName} expects " +
                            $"{paramType.ItemType} but got {checkVal.GetType().Name}");
                    }
                }
                execContext.BindVariable(_parameters[i].Name, arg);
            }

            // Cache the execution plan - optimize only once per function instance
            _cachedPlan ??= new Optimizer.QueryOptimizer()
                .Optimize(_body, new Optimizer.OptimizationContext { Container = default });

            var results = new List<object?>();
            await foreach (var item in _cachedPlan.Root.ExecuteAsync(execContext))
                results.Add(item);

            return results.Count switch
            {
                0 => null,
                1 => results[0],
                _ => results.ToArray()
            };
        }
        finally
        {
            execContext.PopScope(); // parameter scope
            execContext.PopScope(); // closure scope
            execContext.ExitFunctionCall();
        }
    }

    private static bool IsInlineParamPromotion(object value, Ast.ItemType target)
    {
        return target switch
        {
            Ast.ItemType.Double => value is int or long or float or decimal,
            Ast.ItemType.Decimal => value is int or long,
            Ast.ItemType.Float => value is int or long,
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
        // Evaluate fixed arguments eagerly
        var fixedValues = new object?[TotalArity];
        var isPlaceholder = new bool[TotalArity];
        for (int i = 0; i < ArgumentSlots.Count; i++)
        {
            if (ArgumentSlots[i] is { } slot)
            {
                object? val = null;
                await foreach (var item in slot.op.ExecuteAsync(context))
                { val = item; break; }
                fixedValues[i] = val;
            }
            else
            {
                isPlaceholder[i] = true;
            }
        }

        // Resolve the target function if not resolved at compile time
        var func = ResolvedFunc ?? context.Functions.Resolve(FuncName, TotalArity);
        if (func == null)
            throw new XQueryRuntimeException("XPST0017",
                $"Cannot partially apply: function {FuncName.LocalName}#{TotalArity} not found");

        // Validate fixed argument types against function parameter types (XPTY0004)
        var funcParams = func.Parameters;
        for (int i = 0; i < fixedValues.Length; i++)
        {
            if (!isPlaceholder[i] && fixedValues[i] != null && i < funcParams.Count)
            {
                var paramType = funcParams[i].Type;
                if (paramType.ItemType != Ast.ItemType.Item
                    && !TypeCastHelper.MatchesItemType(fixedValues[i]!, paramType.ItemType))
                {
                    throw new XQueryRuntimeException("XPTY0004",
                        $"Partial application of {FuncName.LocalName}(): argument {i + 1} has type " +
                        $"that does not match the required parameter type");
                }
            }
        }

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

        // Evaluate fixed arguments eagerly
        var fixedValues = new object?[TotalArity];
        var isPlaceholder = new bool[TotalArity];
        for (int i = 0; i < ArgumentSlots.Count; i++)
        {
            if (ArgumentSlots[i] is { } slot)
            {
                object? val = null;
                await foreach (var item in slot.op.ExecuteAsync(context))
                { val = item; break; }
                fixedValues[i] = val;
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
        return await _targetFunc.InvokeAsync(mergedArgs, context);
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
        await foreach (var item in FunctionExpression.ExecuteAsync(context))
        { funcVal = item; break; }

        // XPath 3.0: Maps are callable as functions: $map($key) ≡ map:get($map, $key)
        if (funcVal is Dictionary<object, object?> map)
        {
            if (Arguments.Count != 1)
                throw new XQueryRuntimeException("XPTY0004", $"A map used as a function requires exactly 1 argument, but {Arguments.Count} were supplied");
            object? key = null;
            if (Arguments.Count > 0)
            {
                await foreach (var item in Arguments[0].ExecuteAsync(context))
                {
                    key = QueryExecutionContext.AtomizeTyped(item);
                    break;
                }
            }
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
                    key = QueryExecutionContext.Atomize(item);
                    break;
                }
            }
            if (key != null)
            {
                var position = Convert.ToInt32(key);
                if (position >= 1 && position <= array.Count)
                {
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

        var result = await func.InvokeAsync(args, context);

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

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Execute declarations (variable bindings, function registrations)
        foreach (var decl in Declarations)
        {
            await foreach (var _ in decl.ExecuteAsync(context))
            {
                // Declarations consume their results (side effects only)
            }
        }

        // Execute the body
        await foreach (var item in Body.ExecuteAsync(context))
            yield return item;
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

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // For external variables, check if a binding was provided before falling back to the default.
        // Note: type checking of external variable values against declared types (e.g. "as xs:integer")
        // is not performed here because the declared type is not currently propagated to this operator.
        // The parser resolves type annotations during compilation, but VariableDeclarationOperator
        // does not carry an AsType property. Adding runtime type validation would require threading
        // the declared SequenceType through from the AST to this operator.
        if (IsExternal && context.TryGetExternalVariable(VariableName, out var externalValue))
        {
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

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        var func = new InlineFunctionItem(Parameters, Body, context);
        context.Functions.Register(new DeclaredFunction(FunctionName, Parameters, func));
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

    public DeclaredFunction(QName name, IReadOnlyList<FunctionParameter> parameters, InlineFunctionItem implementation)
    {
        _name = name;
        _parameters = parameters;
        _implementation = implementation;
    }

    public override QName Name => _name;
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        _parameters.Select(p => new FunctionParameterDef
        {
            Name = p.Name,
            Type = p.Type ?? XdmSequenceType.ZeroOrMoreItems
        }).ToList();

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return _implementation.InvokeAsync(arguments, context);
    }
}

/// <summary>
/// Helper for type casting and type checking operations.
/// </summary>
public static class TypeCastHelper
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1508")]
    public static object? CastValue(object? value, ItemType targetType)
    {
        if (value == null)
            return null;

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

        return targetType switch
        {
            ItemType.String => value.ToString() ?? "",
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
                string s when s == "INF" => double.PositiveInfinity,
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
            ItemType.UntypedAtomic => value is Xdm.XsUntypedAtomic ? value : new Xdm.XsUntypedAtomic(value?.ToString() ?? ""),
            ItemType.AnyAtomicType => value, // No conversion needed
            ItemType.Duration => value switch
            {
                Xdm.XsDuration d => d,
                TimeSpan ts => new Xdm.XsDuration(0, ts),
                YearMonthDuration ymd => new Xdm.XsDuration(ymd.TotalMonths, TimeSpan.Zero),
                Xdm.XsUntypedAtomic u => Xdm.XsDuration.Parse(u.ToString()),
                string s => Xdm.XsDuration.Parse(s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:duration")
            },
            ItemType.YearMonthDuration => value switch
            {
                YearMonthDuration ymd => ymd,
                Xdm.XsDuration dur => new YearMonthDuration(dur.TotalMonths),
                TimeSpan => new YearMonthDuration(0), // dayTimeDuration has 0 months component
                Xdm.XsUntypedAtomic u => YearMonthDuration.Parse(u.ToString()),
                string s => YearMonthDuration.Parse(s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:yearMonthDuration")
            },
            ItemType.DayTimeDuration => value switch
            {
                TimeSpan ts => ts,
                Xdm.XsDuration dur => dur.DayTime,
                YearMonthDuration => TimeSpan.Zero, // yearMonthDuration has 0 days/time component
                Xdm.XsUntypedAtomic u => ParseDayTimeDuration(u.ToString()),
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
                string s => Xdm.XsDateTime.Parse(s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:dateTime")
            },
            ItemType.Date => value switch
            {
                Xdm.XsDate xd => xd,
                Xdm.XsDateTime xdt => new Xdm.XsDate(DateOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null),
                DateOnly d => new Xdm.XsDate(d, null),
                string s => Xdm.XsDate.Parse(s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:date")
            },
            ItemType.Time => value switch
            {
                Xdm.XsTime xt => xt,
                Xdm.XsDateTime xdt => new Xdm.XsTime(TimeOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null, xdt.FractionalTicks),
                TimeOnly t => new Xdm.XsTime(t, null, (int)(t.Ticks % TimeSpan.TicksPerSecond)),
                string s => Xdm.XsTime.Parse(s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:time")
            },
            ItemType.GYear => value is Xdm.XsGYear ? value : new Xdm.XsGYear(value.ToString()!.Trim()),
            ItemType.GYearMonth => value is Xdm.XsGYearMonth ? value : new Xdm.XsGYearMonth(value.ToString()!.Trim()),
            ItemType.GMonthDay => value is Xdm.XsGMonthDay ? value : new Xdm.XsGMonthDay(value.ToString()!.Trim()),
            ItemType.GDay => value is Xdm.XsGDay ? value : new Xdm.XsGDay(value.ToString()!.Trim()),
            ItemType.GMonth => value is Xdm.XsGMonth ? value : new Xdm.XsGMonth(value.ToString()!.Trim()),
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

    private static QName ParseQName(string s)
    {
        var colonIdx = s.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx > 0)
        {
            var prefix = s[..colonIdx];
            var localName = s[(colonIdx + 1)..];
            var nsId = new NamespaceId((uint)Math.Abs(prefix.GetHashCode()));
            return new QName(nsId, localName, prefix);
        }
        return new QName(NamespaceId.None, s);
    }

    public static bool MatchesType(IReadOnlyList<object?> items, XdmSequenceType type)
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

            // Check typed function type: function(ParamTypes) as ReturnType
            // Uses contravariant parameter types and covariant return type
            if (type.FunctionParameterTypes != null && item is XQueryFunction fn)
            {
                if (!MatchesFunctionType(fn, type.FunctionParameterTypes, type.FunctionReturnType))
                    return false;
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

            // Contravariant: the required param type must be a subtype of the function's param type
            // (the function must accept everything the caller might pass)
            if (!IsSequenceTypeSubtypeOf(reqParamType, fnParamType))
                return false;
        }

        // Check return type (covariant):
        // The function's return type must be a subtype of the required return type
        if (requiredReturnType != null)
        {
            if (!IsSequenceTypeSubtypeOf(fn.ReturnType, requiredReturnType))
                return false;
        }

        return true;
    }

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
            // xs:string subtypes: (xs:NCName, xs:token, etc. — not tracked in our type system)
            _ => false
        };
    }

    /// <summary>
    /// Checks if an item's type annotation matches the required type, considering XSD type hierarchy.
    /// For non-schema-aware processors:
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

    public static bool MatchesItemType(object? item, ItemType type)
    {
        if (item == null)
            return false;
        return type switch
        {
            ItemType.Item => true,
            ItemType.AnyAtomicType => item is not PhoenixmlDb.Xdm.Nodes.XdmNode and not PhoenixmlDb.Xdm.TextNodeItem and not XQueryFunction and not Dictionary<object, object?>,
            ItemType.String => item is string,
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
            ItemType.Function => item is XQueryFunction,
            ItemType.Map => item is Dictionary<object, object?> or IDictionary<object, object?>,
            ItemType.Array => item is List<object?>,
            ItemType.Record => item is Dictionary<object, object?> or IDictionary<object, object?>,
            ItemType.Enum => item is string,
            ItemType.Union => true, // Union matching done in MatchesSequenceItemType with member types
            _ => false
        };
    }

    /// <summary>
    /// Checks if an item matches a full sequence type (including named element/document constraints).
    /// </summary>
    public static bool MatchesSequenceItemType(object? item, XdmSequenceType seqType)
    {
        if (!MatchesItemType(item, seqType.ItemType))
            return false;

        // Check named element constraint: element(name)
        if (seqType.ElementName != null && item is PhoenixmlDb.Xdm.Nodes.XdmElement elem)
        {
            if (elem.LocalName != seqType.ElementName)
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

    public static bool DeepEquals(object? a, object? b, StringComparison stringComparison = StringComparison.Ordinal)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
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
                    return Convert.ToDouble(a) == Convert.ToDouble(b);
                var abi = a is BigInteger ba ? ba : (BigInteger)Convert.ToInt64(a);
                var bbi = b is BigInteger bb ? bb : (BigInteger)Convert.ToInt64(b);
                return abi == bbi;
            }
            var da = Convert.ToDouble(a);
            var db = Convert.ToDouble(b);
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

        return string.Equals(a.ToString(), b.ToString(), stringComparison);
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

            // Phase 4: Evaluate and yield the return clause
            await foreach (var item in ReturnExpr.ExecuteAsync(context))
            {
                yield return item;
            }
        }
        finally
        {
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
                await foreach (var item in part.ExpressionOperator.ExecuteAsync(context))
                {
                    if (item != null)
                        sb.Append(QueryExecutionContext.Atomize(item)?.ToString() ?? "");
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

        var sourceText = QueryExecutionContext.Atomize(sourceValue)?.ToString() ?? "";
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
