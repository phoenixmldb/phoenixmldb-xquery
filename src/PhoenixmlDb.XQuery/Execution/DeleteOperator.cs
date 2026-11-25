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
        // XQUF 3.0 §5.2: zero or more nodes (XUTY0007); a node with no parent is not deleted.
        var targets = new List<object?>();
        await foreach (var item in Target.ExecuteGuarded(context))
            UpdateTargets.Flatten(item, targets);
        foreach (var item in targets)
        {
            if (item is not XdmNode)
                throw new XQueryRuntimeException("XUTY0007",
                    $"delete expression: the target must be a sequence of nodes, not {UpdateTargets.Describe(item)}.");
        }
        foreach (var item in targets)
        {
            if (UpdateTargets.HasParent((XdmNode)item!))
                context.PendingUpdates.AddDelete(item!);
        }

        yield break;
    }
}
