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
