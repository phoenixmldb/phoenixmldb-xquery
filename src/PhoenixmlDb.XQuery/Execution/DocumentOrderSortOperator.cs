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
        var seen = new HashSet<(ulong, NodeId)>();
        await foreach (var item in Input.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (item is XdmNode node)
            {
                if (seen.Add(node.DocumentOrderKey))
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
            items.Sort((a, b) => XdmNode.CompareDocumentOrder((XdmNode)a!, (XdmNode)b!));
        }

        foreach (var item in items)
            yield return item;
    }
}
