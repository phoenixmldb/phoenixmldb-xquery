using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:gMonthDay($arg)</summary>
public sealed class GMonthDayConstructorFunction : TypeConstructorFunction
{
    public GMonthDayConstructorFunction() : base("gMonthDay") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGMonthDay existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGMonthDay(FormatGMonthDay(d.Date.Month, d.Date.Day, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGMonthDay(FormatGMonthDay(dt.Value.Month, dt.Value.Day, dt.HasTimezone ? dt.Value.Offset : null)));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGMonthDay(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gMonthDay value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGMonthDay(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gMonthDay lexical form: --MM-DD(timezone)?</summary>
    private static bool ValidateGMonthDay(string s)
    {
        if (s.Length < 7) return false;
        if (s[0] != '-' || s[1] != '-') return false;
        if (s[2] < '0' || s[2] > '9' || s[3] < '0' || s[3] > '9') return false;
        int month = (s[2] - '0') * 10 + (s[3] - '0');
        if (month < 1 || month > 12) return false;
        if (s[4] != '-') return false;
        if (s[5] < '0' || s[5] > '9' || s[6] < '0' || s[6] > '9') return false;
        int day = (s[5] - '0') * 10 + (s[6] - '0');
        if (day < 1) return false;
        // Max days per month (Feb uses 29 for leap-year-unaware gMonthDay)
        int maxDay = month switch
        {
            2 => 29,
            4 or 6 or 9 or 11 => 30,
            _ => 31
        };
        if (day > maxDay) return false;
        if (s.Length == 7) return true;
        return GYearConstructorFunction.ValidateTimezone(s, 7);
    }

    private static string FormatGMonthDay(int month, int day, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(14);
        sb.Append("--");
        sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('-');
        sb.Append(day.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}
