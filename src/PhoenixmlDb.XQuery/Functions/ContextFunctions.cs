using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:position() as xs:integer
/// </summary>
public sealed class PositionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "position");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is Execution.QueryExecutionContext qec)
        {
            // Access Position which throws XPDY0002 if focus is absent
            return ValueTask.FromResult<object?>(qec.Position);
        }
        return ValueTask.FromResult<object?>(1);
    }
}

/// <summary>
/// fn:last() as xs:integer
/// </summary>
public sealed class LastFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "last");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is Execution.QueryExecutionContext qec)
        {
            return ValueTask.FromResult<object?>(qec.Last);
        }
        return ValueTask.FromResult<object?>(1);
    }
}

/// <summary>
/// Holds a cached snapshot of DateTimeOffset.Now for stable current-dateTime/date/time functions.
/// Per XPath F&amp;O §16.6, these functions must return the same value throughout a single execution scope.
/// Create one instance per transformation and share it across all three current-* function instances.
/// </summary>
public sealed class CurrentDateTimeSnapshot
{
    private DateTimeOffset? _now;

    public DateTimeOffset Now => _now ??= DateTimeOffset.Now;

    /// <summary>Resets the cached value for a new execution scope.</summary>
    public void Reset() => _now = null;
}

/// <summary>
/// fn:current-dateTime() as xs:dateTime
/// </summary>
public sealed class CurrentDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "current-dateTime");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.DateTime, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Read from the execution context — fresh per query, stable within a query
        var now = context is Execution.QueryExecutionContext qec
            ? qec.CurrentDateTime
            : DateTimeOffset.Now;
        return ValueTask.FromResult<object?>(new Xdm.XsDateTime(now, true));
    }
}

/// <summary>
/// fn:current-date() as xs:date
/// </summary>
public sealed class CurrentDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "current-date");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Date, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var now = context is Execution.QueryExecutionContext qec
            ? qec.CurrentDateTime
            : DateTimeOffset.Now;
        return ValueTask.FromResult<object?>(new Xdm.XsDate(DateOnly.FromDateTime(now.DateTime), now.Offset));
    }
}

/// <summary>
/// fn:current-time() as xs:time
/// </summary>
public sealed class CurrentTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "current-time");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Time, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var now = context is Execution.QueryExecutionContext qec
            ? qec.CurrentDateTime
            : DateTimeOffset.Now;
        var fracTicks = (int)(now.Ticks % TimeSpan.TicksPerSecond);
        return ValueTask.FromResult<object?>(new Xdm.XsTime(TimeOnly.FromDateTime(now.DateTime), now.Offset, fracTicks));
    }
}
