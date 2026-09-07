using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:time($arg)</summary>
public sealed class TimeConstructorFunction : TypeConstructorFunction
{
    public TimeConstructorFunction() : base("time") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is XsTime xt) return ValueTask.FromResult<object?>(xt);
        if (arg is TimeOnly t) return ValueTask.FromResult<object?>(new XsTime(t, null, (int)(t.Ticks % TimeSpan.TicksPerSecond)));
        if (arg is XsDateTime xdt) return ValueTask.FromResult<object?>(new XsTime(TimeOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null, xdt.FractionalTicks));
        if (arg is DateTimeOffset dto) return ValueTask.FromResult<object?>(new XsTime(TimeOnly.FromDateTime(dto.DateTime), dto.Offset, (int)(dto.Ticks % TimeSpan.TicksPerSecond)));
        var s = arg.ToString()!.Trim();
        ValidateTimeLexical(s);
        try { return ValueTask.FromResult<object?>(XsTime.Parse(s)); }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:time: {ex.Message}"); }
    }
}
