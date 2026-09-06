using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:yearMonthDuration($arg)</summary>
public sealed class YearMonthDurationConstructorFunction : TypeConstructorFunction
{
    public YearMonthDurationConstructorFunction() : base("yearMonthDuration") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.YearMonthDuration ymd) return ValueTask.FromResult<object?>(ymd);
        // xs:duration → xs:yearMonthDuration: extract month component, discard day-time
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>(new Xdm.YearMonthDuration(dur.TotalMonths));
        // xs:dayTimeDuration → xs:yearMonthDuration: always P0M (no month component)
        if (arg is TimeSpan)
            return ValueTask.FromResult<object?>(new Xdm.YearMonthDuration(0));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        ValidateYearMonthDurationString(s);
        try { return ValueTask.FromResult<object?>(Xdm.YearMonthDuration.Parse(s)); }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: {ex.Message}"); }
    }

    /// <summary>Validates yearMonthDuration lexical form: [-]P[nY][nM], no timezone, no day-time, must have at least Y or M.</summary>
    private static void ValidateYearMonthDurationString(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        if (i >= s.Length || s[i] != 'P')
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: not a valid duration");
        i++; // skip P
        // After P, expect digits followed by Y and/or digits followed by M, nothing else
        bool hasDesignator = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (c >= '0' && c <= '9') { i++; continue; }
            if (c == 'Y' || c == 'M') { hasDesignator = true; i++; continue; }
            // Any other character (T, D, H, S, +, Z, etc.) is invalid
            if (c == 'D' || c == 'T' || c == 'H' || c == 'S')
                throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: day-time component not allowed");
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: invalid character '{c}'");
        }
        if (!hasDesignator)
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: must contain Y or M designator");
    }
}
