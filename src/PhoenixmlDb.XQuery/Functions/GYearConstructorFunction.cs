using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:gYear($arg)</summary>
public sealed class GYearConstructorFunction : TypeConstructorFunction
{
    public GYearConstructorFunction() : base("gYear") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGYear existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGYear(FormatGYear(d.EffectiveYear, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGYear(FormatGYear(dt.EffectiveYear, dt.HasTimezone ? dt.Value.Offset : null)));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGYear(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gYear value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGYear(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gYear lexical form: -?[0-9]{4,}(Z|[+-][0-9]{2}:[0-9]{2})?</summary>
    private static bool ValidateGYear(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        // Leading '+' is not allowed per XSD
        if (i < s.Length && s[i] == '+') return false;
        int digitStart = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        int digitCount = i - digitStart;
        // Must have at least 4 digits
        if (digitCount < 4) return false;
        // Leading zeros only allowed if digit count is exactly 4
        if (digitCount > 4 && s[digitStart] == '0') return false;
        // Remainder must be empty or a valid timezone
        if (i == s.Length) return true;
        return ValidateTimezone(s, i);
    }

    /// <summary>Validates timezone suffix: Z | [+-]HH:MM</summary>
    internal static bool ValidateTimezone(string s, int i)
    {
        if (i >= s.Length) return true;
        if (s[i] == 'Z') return i + 1 == s.Length;
        if (s[i] != '+' && s[i] != '-') return false;
        i++;
        // HH:MM
        if (i + 5 != s.Length) return false;
        if (s[i] < '0' || s[i] > '9' || s[i + 1] < '0' || s[i + 1] > '9') return false;
        if (s[i + 2] != ':') return false;
        if (s[i + 3] < '0' || s[i + 3] > '9' || s[i + 4] < '0' || s[i + 4] > '9') return false;
        int hh = (s[i] - '0') * 10 + (s[i + 1] - '0');
        int mm = (s[i + 3] - '0') * 10 + (s[i + 4] - '0');
        return hh <= 14 && mm <= 59 && !(hh == 14 && mm > 0);
    }

    private static string FormatGYear(long year, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(16);
        if (year < 0) { sb.Append('-'); sb.Append((-year).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)); }
        else sb.Append(year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}
