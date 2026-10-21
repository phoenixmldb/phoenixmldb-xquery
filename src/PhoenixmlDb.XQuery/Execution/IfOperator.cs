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

    // Without an async iterator per evaluation where the condition and both branches allow it: an
    // if expression blocked the synchronous path for everything above it. A loop of 100,000
    // xsl:evaluate calls of `if (position() mod 7 = 0) then $a else $b` allocated ~700 MB of
    // iterator state machines (W3C evaluate-010).
    private bool? _supportsSync;
    internal override bool SupportsSync => _supportsSync ??= Condition.SupportsSync && Then.SupportsSync && Else.SupportsSync;

    // As ExecuteAsync: the condition's first item decides.
    internal override object? EvaluateSync(QueryExecutionContext context)
        => (QueryExecutionContext.EffectiveBooleanValue(FirstSyncItem(Condition.EvaluateSync(context))) ? Then : Else)
            .EvaluateSync(context);

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
