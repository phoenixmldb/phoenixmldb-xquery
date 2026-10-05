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
/// Evaluates axis navigation + predicate filtering per input node, then
/// deduplicates and sorts the combined results.
/// This is necessary for XPath semantics: in a path like A//B[1], the predicate [1]
/// must apply per-parent (first B child of each node in A//), not to the flattened set.
/// </summary>
public sealed class PerNodeStepOperator : PhysicalOperator
{
    public required PhysicalOperator Input { get; init; }

    // A non-node from the left operand of `/` is XPTY0019; one as the context item of a bare
    // step is XPTY0020. Every case was XPTY0020 (QT3 XPTY0019_1/2/3: <a/>/1/node(), $x/a).
    private string NonNodeInputCode
        => Input is ContextItemOperator { IsImplicitStepInput: true } ? "XPTY0020" : "XPTY0019";
    public required Axis Axis { get; init; }
    public required NodeTest NodeTest { get; init; }
    public required IReadOnlyList<PhysicalOperator> PredicateOperators { get; init; }
    public required IReadOnlyList<bool> PredicatePositional { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var results = new List<XdmNode>();
        var seen = new HashSet<(ulong, NodeId)>();

        await foreach (var item in Input.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (item is not XdmNode inputNode)
            {
                if (item != null)
                    throw new PhoenixmlDb.XQuery.Functions.XQueryException(NonNodeInputCode,
                        $"An axis step ({Axis}::{NodeTest}) was used when the context item is not a node (got {DescribeItemType(item)})",
                        Location);
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
                if (fi is XdmNode node && seen.Add(node.DocumentOrderKey))
                    results.Add(node);
            }
        }

        // Sort combined results into document order
        if (results.Count > 1)
            results.Sort(XdmNode.CompareDocumentOrder);
        foreach (var r in results)
            yield return r;
    }

    private static bool MatchesPredicate(object? result, int position)
        => PositionalPredicate.Selects(result, position);

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
        KindTest kt => AxisNavigationOperator.MatchesKindTest(node, kt, context),
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

    // Document-order walk with an explicit stack of child enumerators, not recursion (#95). The
    // recursive form nested one iterator per level — O(depth) work per node yielded — and so capped
    // tree DEPTH with the function-call recursion limit (1000): `//x` over a 1,100-deep document
    // raised FOER0000, and XDM trees cannot contain the cycles that message suggested.
    private static IEnumerable<XdmNode> GetDescendants(XdmNode node, QueryExecutionContext context, bool includeSelf, int depth)
    {
        if (includeSelf)
            yield return node;
        var stack = new Stack<IEnumerator<XdmNode>>();
        try
        {
            stack.Push(GetChildren(node, context).GetEnumerator());
            while (stack.Count > 0)
            {
                var children = stack.Peek();
                if (!children.MoveNext())
                {
                    stack.Pop().Dispose();
                    continue;
                }
                var child = children.Current;
                yield return child;
                stack.Push(GetChildren(child, context).GetEnumerator());
            }
        }
        finally
        {
            while (stack.Count > 0)
                stack.Pop().Dispose();
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
