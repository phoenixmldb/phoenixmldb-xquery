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
/// Count clause operator.
/// </summary>
public sealed class CountClauseOperator : FlworClauseOperator
{
    public required QName Variable { get; init; }
    private long _counter;

    public long CurrentCount => _counter;
    public void IncrementCounter() => _counter++;
    public void ResetCounter() => _counter = 0;

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        IncrementCounter();
        yield return new Dictionary<QName, object?> { [Variable] = CurrentCount };
    }
}
