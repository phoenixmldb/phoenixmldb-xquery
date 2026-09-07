using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:gMonth($arg)</summary>
public sealed class GMonthConstructorFunction : TypeConstructorFunction
{
    public GMonthConstructorFunction() : base("gMonth") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGMonth existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGMonth(FormatGMonth(d.Date.Month, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGMonth(FormatGMonth(dt.Value.Month, dt.HasTimezone ? dt.Value.Offset : null)));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGMonth(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gMonth value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGMonth(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gMonth lexical form: --MM(timezone)?</summary>
    private static bool ValidateGMonth(string s)
    {
        if (s.Length < 4) return false;
        if (s[0] != '-' || s[1] != '-') return false;
        if (s[2] < '0' || s[2] > '9' || s[3] < '0' || s[3] > '9') return false;
        int month = (s[2] - '0') * 10 + (s[3] - '0');
        if (month < 1 || month > 12) return false;
        if (s.Length == 4) return true;
        return GYearConstructorFunction.ValidateTimezone(s, 4);
    }

    private static string FormatGMonth(int month, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(12);
        sb.Append("--");
        sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}
