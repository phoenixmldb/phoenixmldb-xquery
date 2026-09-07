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
/// Window clause operator (tumbling or sliding window).
/// </summary>
public sealed class WindowClauseOperator : FlworClauseOperator
{
    public required Ast.WindowKind Kind { get; init; }
    public required QName Variable { get; init; }
    public Ast.XdmSequenceType? TypeDeclaration { get; init; }
    public bool OnlyEnd { get; init; }
    public required PhysicalOperator InputOperator { get; init; }
    public required WindowConditionOperator StartCondition { get; init; }
    public WindowConditionOperator? EndCondition { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        // Materialize the input sequence
        var items = new List<object?>();
        await foreach (var item in InputOperator.ExecuteAsync(context))
            items.Add(item);

        if (Kind == Ast.WindowKind.Tumbling)
        {
            await foreach (var window in ExecuteTumblingAsync(items, context))
                yield return window;
        }
        else
        {
            await foreach (var window in ExecuteSlidingAsync(items, context))
                yield return window;
        }
    }

    private async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteTumblingAsync(
        List<object?> items, QueryExecutionContext context)
    {
        bool inWindow = false;
        int windowStartPos = 0;
        object? windowStartItem = null;
        object? windowStartPrev = null;
        object? windowStartNext = null;
        var windowItems = new List<object?>();

        for (int i = 0; i < items.Count; i++)
        {
            var cur = items[i];
            var prev = i > 0 ? items[i - 1] : null;
            var next = i + 1 < items.Count ? items[i + 1] : null;
            var pos = i + 1; // 1-based

            if (!inWindow)
            {
                // No window open — check start condition
                if (await EvaluateConditionAsync(StartCondition, cur, prev, next, pos, context))
                {
                    inWindow = true;
                    windowStartPos = pos;
                    windowStartItem = cur;
                    windowStartPrev = prev;
                    windowStartNext = next;
                    windowItems.Clear();
                    windowItems.Add(cur);

                    // For windows with an end condition, check if end fires on the start item too
                    if (EndCondition != null)
                    {
                        bool shouldEnd = await EvaluateConditionWithStartVarsAsync(
                            EndCondition, cur, prev, next, pos,
                            StartCondition, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, context);
                        if (shouldEnd)
                        {
                            var t = MakeWindowTuple(windowItems, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, cur, prev, next, pos);
                            if (t != null) yield return t;
                            inWindow = false;
                            windowItems.Clear();
                        }
                    }
                }
            }
            else
            {
                // Window is open — add item and check end condition
                windowItems.Add(cur);

                if (EndCondition != null)
                {
                    bool shouldEnd = await EvaluateConditionWithStartVarsAsync(
                        EndCondition, cur, prev, next, pos,
                        StartCondition, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, context);
                    if (shouldEnd)
                    {
                        var t = MakeWindowTuple(windowItems, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, cur, prev, next, pos);
                        if (t != null) yield return t;
                        inWindow = false;
                        windowItems.Clear();
                    }
                }
                else
                {
                    // No end condition — check if a new start condition fires (closes current window)
                    bool startNew = await EvaluateConditionAsync(StartCondition, cur, prev, next, pos, context);
                    if (startNew)
                    {
                        // Remove the current item from old window — it starts the new one
                        windowItems.RemoveAt(windowItems.Count - 1);
                        if (windowItems.Count > 0)
                        {
                            // Last closed item is the one before the new start (at index i-1).
                            var lastIdx = i - 1; // 0-based
                            var lastItem = items[lastIdx];
                            var lastPrev = lastIdx > 0 ? items[lastIdx - 1] : null;
                            var lastNext = cur;
                            var t = MakeWindowTuple(windowItems, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, lastItem, lastPrev, lastNext, lastIdx + 1);
                            if (t != null) yield return t;
                        }

                        windowStartPos = pos;
                        windowStartItem = cur;
                        windowStartPrev = prev;
                        windowStartNext = next;
                        windowItems.Clear();
                        windowItems.Add(cur);
                    }
                }
            }
        }

        // Flush remaining window (only if not "only end" — an unclosed window is dropped under "only end")
        if (inWindow && windowItems.Count > 0 && !OnlyEnd)
        {
            // Per spec, end variables bind to the last item when the sequence is exhausted.
            var lastIdx = items.Count - 1;
            var lastItem = items[lastIdx];
            var lastPrev = lastIdx > 0 ? items[lastIdx - 1] : null;
            object? lastNext = null;
            var t = MakeWindowTuple(windowItems, windowStartItem, windowStartPrev, windowStartNext, windowStartPos, lastItem, lastPrev, lastNext, lastIdx + 1);
            if (t != null) yield return t;
        }
    }

    private async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteSlidingAsync(
        List<object?> items, QueryExecutionContext context)
    {
        // Sliding windows: for each position where start condition is true,
        // open a new window. Each window independently ends where end condition is true.
        for (int i = 0; i < items.Count; i++)
        {
            var sCur = items[i];
            var sPrev = i > 0 ? items[i - 1] : null;
            var sNext = i + 1 < items.Count ? items[i + 1] : null;
            var sPos = i + 1;

            if (!await EvaluateConditionAsync(StartCondition, sCur, sPrev, sNext, sPos, context))
                continue;

            // Start a window here
            var windowItems = new List<object?> { sCur };
            bool ended = false;
            object? endCur = null, endPrev = null, endNext = null;
            int endPos = 0;

            // Check end condition on the start item itself (zero-length windows)
            if (EndCondition != null &&
                await EvaluateConditionWithStartVarsAsync(
                    EndCondition, sCur, sPrev, sNext, sPos,
                    StartCondition, sCur, sPrev, sNext, sPos, context))
            {
                ended = true;
                endCur = sCur; endPrev = sPrev; endNext = sNext; endPos = sPos;
            }
            else
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    var eCur = items[j];
                    var ePrev = items[j - 1];
                    var eNext = j + 1 < items.Count ? items[j + 1] : null;
                    var ePos = j + 1;

                    windowItems.Add(eCur);

                    if (EndCondition != null &&
                        await EvaluateConditionWithStartVarsAsync(
                            EndCondition, eCur, ePrev, eNext, ePos,
                            StartCondition, sCur, sPrev, sNext, sPos, context))
                    {
                        ended = true;
                        endCur = eCur; endPrev = ePrev; endNext = eNext; endPos = ePos;
                        break;
                    }
                }
            }

            if (ended)
            {
                var t = MakeWindowTuple(windowItems, sCur, sPrev, sNext, sPos, endCur, endPrev, endNext, endPos);
                if (t != null) yield return t;
            }
            else if (!OnlyEnd)
            {
                // No end condition fired. Emit the window (unclosed) unless OnlyEnd is set.
                // Per spec, end variables bind to the last item when sequence is exhausted.
                var lastIdx = items.Count - 1;
                var lastItem = items[lastIdx];
                var lastPrev = lastIdx > 0 ? items[lastIdx - 1] : null;
                object? lastNext = null;
                var t = MakeWindowTuple(windowItems, sCur, sPrev, sNext, sPos, lastItem, lastPrev, lastNext, lastIdx + 1);
                if (t != null) yield return t;
            }
        }
    }

    private Dictionary<QName, object?>? MakeWindowTuple(
        List<object?> windowItems,
        object? startItem, object? startPrev, object? startNext, int startPos,
        object? endItem, object? endPrev, object? endNext, int endPos)
    {
        // Build window value. Always expose as a sequence (list).
        // If the window is a single item, still pass it as-is for scalar access but wrap list for count().
        object? value;
        if (windowItems.Count == 0) value = null;
        else if (windowItems.Count == 1) value = windowItems[0];
        else value = windowItems.ToArray();

        // Enforce type declaration on the window variable.
        if (TypeDeclaration != null)
        {
            if (!CheckWindowType(value, windowItems.Count, TypeDeclaration))
                throw new XQueryRuntimeException("XPTY0004", "Window value does not match declared type");
        }

        var tuple = new Dictionary<QName, object?> { [Variable] = value };

        if (StartCondition.CurrentItem is { } sCurVar) tuple[sCurVar] = startItem;
        if (StartCondition.Position is { } sPosVar) tuple[sPosVar] = (long)startPos;
        if (StartCondition.PreviousItem is { } sPrevVar) tuple[sPrevVar] = startPrev;
        if (StartCondition.NextItem is { } sNextVar) tuple[sNextVar] = startNext;

        if (EndCondition != null)
        {
            if (EndCondition.CurrentItem is { } eCurVar) tuple[eCurVar] = endItem;
            if (EndCondition.Position is { } ePosVar) tuple[ePosVar] = (long)endPos;
            if (EndCondition.PreviousItem is { } ePrevVar) tuple[ePrevVar] = endPrev;
            if (EndCondition.NextItem is { } eNextVar) tuple[eNextVar] = endNext;
        }

        return tuple;
    }

    private static bool CheckWindowType(object? value, int count, Ast.XdmSequenceType type)
    {
        var items = new List<object?>();
        if (value != null)
        {
            if (value is string) items.Add(value);
            else if (value is System.Collections.IEnumerable e)
            {
                foreach (var x in e) items.Add(x);
            }
            else items.Add(value);
        }
        return TypeCastHelper.MatchesType(items, type);
    }

    /// <summary>
    /// Evaluates the end condition with start condition variables also bound,
    /// so end conditions can reference $spos, $s, etc. from the start clause.
    /// </summary>
    private static async ValueTask<bool> EvaluateConditionWithStartVarsAsync(
        WindowConditionOperator endCondition,
        object? cur, object? prev, object? next, int pos,
        WindowConditionOperator startCondition,
        object? startItem, object? startPrev, object? startNext, int startPos,
        QueryExecutionContext context)
    {
        context.PushScope();
        try
        {
            // Bind start condition variables so end condition can reference them
            if (startCondition.CurrentItem.HasValue)
                context.BindVariable(startCondition.CurrentItem.Value, startItem);
            if (startCondition.Position.HasValue)
                context.BindVariable(startCondition.Position.Value, (long)startPos);
            if (startCondition.PreviousItem.HasValue)
                context.BindVariable(startCondition.PreviousItem.Value, startPrev);
            if (startCondition.NextItem.HasValue)
                context.BindVariable(startCondition.NextItem.Value, startNext);

            // Bind end condition variables
            if (endCondition.CurrentItem.HasValue)
                context.BindVariable(endCondition.CurrentItem.Value, cur);
            if (endCondition.PreviousItem.HasValue)
                context.BindVariable(endCondition.PreviousItem.Value, prev);
            if (endCondition.NextItem.HasValue)
                context.BindVariable(endCondition.NextItem.Value, next);
            if (endCondition.Position.HasValue)
                context.BindVariable(endCondition.Position.Value, (long)pos);

            object? result = null;
            await foreach (var item in endCondition.WhenOperator.ExecuteAsync(context))
            {
                result = item;
                break;
            }
            return QueryExecutionContext.EffectiveBooleanValue(result);
        }
        finally
        {
            context.PopScope();
        }
    }

    private static async ValueTask<bool> EvaluateConditionAsync(
        WindowConditionOperator condition,
        object? cur, object? prev, object? next, int pos,
        QueryExecutionContext context)
    {
        context.PushScope();
        try
        {
            if (condition.CurrentItem.HasValue)
                context.BindVariable(condition.CurrentItem.Value, cur);
            if (condition.PreviousItem.HasValue)
                context.BindVariable(condition.PreviousItem.Value, prev);
            if (condition.NextItem.HasValue)
                context.BindVariable(condition.NextItem.Value, next);
            if (condition.Position.HasValue)
                context.BindVariable(condition.Position.Value, (long)pos);

            object? result = null;
            await foreach (var item in condition.WhenOperator.ExecuteAsync(context))
            {
                result = item;
                break;
            }
            return QueryExecutionContext.EffectiveBooleanValue(result);
        }
        finally
        {
            context.PopScope();
        }
    }
}
