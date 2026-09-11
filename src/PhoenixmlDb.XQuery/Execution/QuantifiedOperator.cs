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
/// Quantified expression: some/every $x in ... satisfies ...
/// </summary>
public sealed class QuantifiedOperator : PhysicalOperator
{
    public required Quantifier Quantifier { get; init; }
    public required IReadOnlyList<QuantifiedBindingOperator> Bindings { get; init; }
    public required PhysicalOperator Satisfies { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var result = await EvaluateBindingsAsync(context, 0);
        yield return result;
    }

    private async Task<bool> EvaluateBindingsAsync(QueryExecutionContext context, int index)
    {
        if (index >= Bindings.Count)
        {
            // All bindings established, evaluate satisfies
            // Collect all items for proper EBV (multi-item sequences → FORG0006)
            var satItems = new List<object?>();
            await foreach (var item in Satisfies.ExecuteAsync(context))
                satItems.Add(item);
            object? satVal = satItems.Count == 0 ? null
                : satItems.Count == 1 ? satItems[0]
                : satItems.ToArray();
            return QueryExecutionContext.EffectiveBooleanValue(satVal);
        }

        var binding = Bindings[index];
        var items = new List<object?>();
        await foreach (var item in binding.InputOperator.ExecuteAsync(context))
            items.Add(item);

        // Enforce type declaration on each bound item (XQuery §3.12.1)
        void CheckType(object? item)
        {
            if (binding.TypeDeclaration is { } td)
            {
                if (item != null && !TypeCastHelper.MatchesItemType(item, td.ItemType))
                    throw new XQueryRuntimeException("XPTY0004",
                        $"quantified ${binding.Variable.LocalName}: value does not match declared type {td}");
            }
        }

        if (Quantifier == Quantifier.Some)
        {
            foreach (var item in items)
            {
                // Poll on EVERY item. This was once per 16,384 items, which bounds the gap in
                // ITERATIONS, not time: with a body that never polls — one builtin call, or QT3
                // same-key-023's map:remove/map:put — a slow body made the gap minutes, and
                // same-key-023 overran a 30 s timeout by ~20 minutes. The poll is a field read.
                context.CancellationToken.ThrowIfCancellationRequested();
                CheckType(item);
                context.PushScope();
                context.BindVariable(binding.Variable, item);
                try
                {
                    if (await EvaluateBindingsAsync(context, index + 1))
                        return true;
                }
                finally { context.PopScope(); }
            }
            return false;
        }
        else // Every
        {
            foreach (var item in items)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                CheckType(item);
                context.PushScope();
                context.BindVariable(binding.Variable, item);
                try
                {
                    if (!await EvaluateBindingsAsync(context, index + 1))
                        return false;
                }
                finally { context.PopScope(); }
            }
            return true;
        }
    }
}
