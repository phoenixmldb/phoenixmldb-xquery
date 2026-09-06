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
