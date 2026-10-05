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

    /// <summary>
    /// The sorted result is the left operand of a further `/`, so it must be all nodes: any
    /// non-node is XPTY0019 (XPath 3.1 §3.3.1.1). The mixture error (XPTY0018) is for the
    /// FINAL step's result; raising it here preempted the left operand's error (QT3 XPTY0019_3).
    /// </summary>
    public bool IsLeftOperandOfPath { get; init; }

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

        if (!allNodes && IsLeftOperandOfPath)
            throw new Functions.XQueryException("XPTY0019",
                "The left operand of '/' contains an item that is not a node");

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
