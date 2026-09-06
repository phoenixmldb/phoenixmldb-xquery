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
/// If expression operator.
/// </summary>
public sealed class IfOperator : PhysicalOperator
{
    public required PhysicalOperator Condition { get; init; }
    public required PhysicalOperator Then { get; init; }
    public required PhysicalOperator Else { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? condValue = null;
        await foreach (var item in Condition.ExecuteAsync(context))
        {
            condValue = item;
            break;
        }

        var branch = QueryExecutionContext.EffectiveBooleanValue(condValue) ? Then : Else;

        await foreach (var item in branch.ExecuteAsync(context))
        {
            yield return item;
        }
    }
}
