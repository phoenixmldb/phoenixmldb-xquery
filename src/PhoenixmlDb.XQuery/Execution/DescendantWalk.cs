using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// The descendants of a node in document order, walked with an explicit stack, not recursion
/// (#95: the recursive form capped tree depth at the function-call limit). The stack holds each
/// open node's child list and the position reached in it. It held an iterator per open node,
/// one allocated for every node visited, element or leaf: the ISO Schematron SVRL step over
/// NY.EMS.v350 allocated ~1.1 GB of them (xquery#294).
/// </summary>
internal static class DescendantWalk
{
    internal static IEnumerable<XdmNode> Of(XdmNode node, QueryExecutionContext context, bool includeSelf)
    {
        if (includeSelf)
            yield return node;
        if (ChildIds(node) is not { Count: > 0 } top)
            yield break;
        var stack = new Stack<(IReadOnlyList<NodeId> Ids, int Next)>();
        stack.Push((top, 0));
        while (stack.Count > 0)
        {
            var (ids, next) = stack.Pop();
            if (next >= ids.Count)
                continue;
            stack.Push((ids, next + 1));
            var child = context.LoadNode(ids[next]);
            if (child == null)
                continue;
            yield return child;
            if (ChildIds(child) is { Count: > 0 } grandchildren)
                stack.Push((grandchildren, 0));
        }
    }

    private static IReadOnlyList<NodeId>? ChildIds(XdmNode node) => node switch
    {
        XdmElement elem => elem.Children,
        XdmDocument doc => doc.Children,
        _ => null,
    };
}
