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

    /// <summary>
    /// When true, this is a path step (/) rather than a simple map (!),
    /// and non-node context items must raise XPTY0019.
    /// </summary>
    public bool IsPathStep { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        if (!RequiresPositionalAccess)
        {
            var position = 0;
            await foreach (var item in Left.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (IsPathStep && item is not XdmNode)
                    throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0019",
                        "The context item for an axis step is not a node");
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
                if (IsPathStep && item is not XdmNode)
                    throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0019",
                        "The context item for an axis step is not a node");
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
