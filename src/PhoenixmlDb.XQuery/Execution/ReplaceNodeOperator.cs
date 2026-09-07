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
