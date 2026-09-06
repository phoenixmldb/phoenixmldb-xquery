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
/// Sequence operator.
/// </summary>
public sealed class SequenceOperator : PhysicalOperator
{
    public required IReadOnlyList<PhysicalOperator> Items { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        foreach (var item in Items)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await foreach (var result in item.ExecuteAsync(context))
            {
                yield return result;
            }
        }
    }
}
