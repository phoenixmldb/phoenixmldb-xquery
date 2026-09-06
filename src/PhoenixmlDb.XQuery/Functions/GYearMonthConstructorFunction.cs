using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:gYearMonth($arg)</summary>
public sealed class GYearMonthConstructorFunction : TypeConstructorFunction
{
    public GYearMonthConstructorFunction() : base("gYearMonth") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGYearMonth existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGYearMonth(FormatGYearMonth(d.EffectiveYear, d.Date.Month, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGYearMonth(FormatGYearMonth(dt.EffectiveYear, dt.Value.Month, dt.HasTimezone ? dt.Value.Offset : null)));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGYearMonth(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gYearMonth value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGYearMonth(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gYearMonth lexical form: -?YYYY-MM(timezone)?</summary>
    private static bool ValidateGYearMonth(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        if (i < s.Length && s[i] == '+') return false;
        int digitStart = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        int digitCount = i - digitStart;
        if (digitCount < 4) return false;
        if (digitCount > 4 && s[digitStart] == '0') return false;
        // Must have -MM
        if (i >= s.Length || s[i] != '-') return false;
        i++;
        if (i + 2 > s.Length) return false;
        if (s[i] < '0' || s[i] > '9' || s[i + 1] < '0' || s[i + 1] > '9') return false;
        int month = (s[i] - '0') * 10 + (s[i + 1] - '0');
        if (month < 1 || month > 12) return false;
        i += 2;
        if (i == s.Length) return true;
        return GYearConstructorFunction.ValidateTimezone(s, i);
    }

    private static string FormatGYearMonth(long year, int month, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(20);
        if (year < 0) { sb.Append('-'); sb.Append((-year).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)); }
        else sb.Append(year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('-');
        sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}
