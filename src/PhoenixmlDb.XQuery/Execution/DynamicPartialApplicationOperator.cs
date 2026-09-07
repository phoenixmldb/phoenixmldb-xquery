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
/// Partial application of a dynamic function expression: $f(?, $p) → function($a) { $f($a, $p) }
/// </summary>
public sealed class DynamicPartialApplicationOperator : PhysicalOperator
{
    public required PhysicalOperator FuncExpression { get; init; }
    public required IReadOnlyList<(int index, PhysicalOperator op)?> ArgumentSlots { get; init; }
    public required int TotalArity { get; init; }
    public required int PlaceholderCount { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate the function expression
        object? funcVal = null;
        await foreach (var item in FuncExpression.ExecuteAsync(context))
        { funcVal = item; break; }

        if (funcVal is not XQueryFunction func)
            throw new XQueryRuntimeException("XPTY0004", "Value is not a function for partial application");

        // Arity must match: a function reference of arity N partially applied with M args
        // requires N == M (some of which are placeholders).
        if (!func.IsVariadic && TotalArity != func.Arity)
            throw new XQueryRuntimeException("XPTY0004",
                $"Function {func.Name.LocalName}() expects {func.Arity} argument(s), but {TotalArity} were supplied");

        // Evaluate fixed arguments eagerly, materializing the full sequence for each slot.
        var fixedValues = new object?[TotalArity];
        var isPlaceholder = new bool[TotalArity];
        for (int i = 0; i < ArgumentSlots.Count; i++)
        {
            if (ArgumentSlots[i] is { } slot)
            {
                object? singleItem = null;
                List<object?>? multiItems = null;
                var count = 0;
                await foreach (var item in slot.op.ExecuteAsync(context))
                {
                    count++;
                    if (count == 1) singleItem = item;
                    else if (count == 2) multiItems = [singleItem, item];
                    else multiItems!.Add(item);
                    if (count % 65536 == 0) context.CheckMaterializationLimit(count);
                }
                fixedValues[i] = count switch
                {
                    0 => null,
                    1 => singleItem,
                    _ => multiItems!.ToArray()
                };
            }
            else
            {
                isPlaceholder[i] = true;
            }
        }

        yield return new PartiallyAppliedItem(func, fixedValues, isPlaceholder, PlaceholderCount);
    }
}
