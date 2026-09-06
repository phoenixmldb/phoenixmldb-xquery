using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:duration($arg)</summary>
public sealed class DurationConstructorFunction : TypeConstructorFunction
{
    public DurationConstructorFunction() : base("duration") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDuration d) return ValueTask.FromResult<object?>(d);
        // xs:dayTimeDuration → xs:duration (zero months, preserve day-time)
        if (arg is TimeSpan ts)
            return ValueTask.FromResult<object?>(new Xdm.XsDuration(0, ts));
        if (arg is Xdm.DayTimeDuration dtd)
            return ValueTask.FromResult<object?>(new Xdm.XsDuration(0, dtd.ToTimeSpan()));
        // xs:yearMonthDuration → xs:duration (preserve months, zero day-time)
        if (arg is Xdm.YearMonthDuration ymd)
            return ValueTask.FromResult<object?>(new Xdm.XsDuration(ymd.TotalMonths, TimeSpan.Zero));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        ValidateDurationLexical(s);
        try { return ValueTask.FromResult<object?>(Xdm.XsDuration.Parse(s)); }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:duration: {ex.Message}"); }
    }
}
