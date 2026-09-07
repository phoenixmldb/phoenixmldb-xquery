using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:dayTimeDuration($arg)</summary>
public sealed class DayTimeDurationConstructorFunction : TypeConstructorFunction
{
    public DayTimeDurationConstructorFunction() : base("dayTimeDuration") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.DayTimeDuration dtd) return ValueTask.FromResult<object?>(dtd);
        if (arg is TimeSpan ts) return ValueTask.FromResult<object?>(ts);
        // xs:duration → xs:dayTimeDuration: extract day-time component, discard months
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>(dur.DayTime);
        // xs:yearMonthDuration → xs:dayTimeDuration: always PT0S (no day-time component)
        if (arg is Xdm.YearMonthDuration)
            return ValueTask.FromResult<object?>(TimeSpan.Zero);
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        // Validate duration lexical form first (rejects bare P, decimal without digits, etc.)
        ValidateDurationLexical(s);
        // dayTimeDuration must not contain Y or M (month) components
        ValidateDayTimeDurationString(s);
        // Try TimeSpan first (most common case), fall back to DayTimeDuration for overflow
        try
        {
            return ValueTask.FromResult<object?>(XmlConvert.ToTimeSpan(s));
        }
        catch (OverflowException)
        {
            try { return ValueTask.FromResult<object?>(Xdm.DayTimeDuration.Parse(s)); }
            catch (XQueryRuntimeException) { throw; }
            catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: {ex.Message}"); }
        }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: {ex.Message}"); }
    }

    /// <summary>Validates that a dayTimeDuration string does not contain Y or M (month) components.</summary>
    private static void ValidateDayTimeDurationString(string s)
    {
        // dayTimeDuration lexical form: [-]P[nD][T[nH][nM][nS]]
        // Must NOT contain Y or M-before-T (month) components
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        if (i >= s.Length || s[i] != 'P')
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: not a valid duration");
        i++; // skip P
        // Scan before T — only digits and D allowed, not Y or M
        while (i < s.Length && s[i] != 'T')
        {
            if (s[i] == 'Y')
                throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: year component not allowed");
            if (s[i] == 'M')
                throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: month component not allowed");
            i++;
        }
    }
}
