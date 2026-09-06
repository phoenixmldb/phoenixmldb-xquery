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
/// Typeswitch expression.
/// </summary>
public sealed class TypeswitchOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required IReadOnlyList<TypeswitchCaseOperator> Cases { get; init; }
    public QName? DefaultVariable { get; init; }
    public required PhysicalOperator DefaultResult { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var items = new List<object?>();
        await foreach (var item in Operand.ExecuteAsync(context))
            items.Add(item);

        object? operandVal = items.Count switch
        {
            0 => null,
            1 => items[0],
            _ => items.ToArray()
        };

        foreach (var @case in Cases)
        {
            foreach (var type in @case.Types)
            {
                if (TypeCastHelper.MatchesType(items, type))
                {
                    context.PushScope();
                    if (@case.Variable.HasValue)
                        context.BindVariable(@case.Variable.Value, operandVal);
                    try
                    {
                        await foreach (var result in @case.Result.ExecuteAsync(context))
                            yield return result;
                    }
                    finally { context.PopScope(); }
                    yield break;
                }
            }
        }

        // Default case
        context.PushScope();
        if (DefaultVariable.HasValue)
            context.BindVariable(DefaultVariable.Value, operandVal);
        try
        {
            await foreach (var result in DefaultResult.ExecuteAsync(context))
                yield return result;
        }
        finally { context.PopScope(); }
    }
}
