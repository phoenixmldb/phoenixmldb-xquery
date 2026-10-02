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
/// Returns a constant value.
/// </summary>
public sealed class ConstantOperator : PhysicalOperator
{
    public required object? Value { get; init; }

    // A constant that is itself a sequence would read as several items here; it keeps the async path.
    internal override bool SupportsSync => Value is not object?[];

    internal override object? EvaluateSync(QueryExecutionContext context) => Value;

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        yield return Value;
    }
}
