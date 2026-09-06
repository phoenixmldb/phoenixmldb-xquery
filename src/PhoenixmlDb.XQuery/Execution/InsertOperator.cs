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
