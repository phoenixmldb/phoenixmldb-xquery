using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:gDay($arg)</summary>
public sealed class GDayConstructorFunction : TypeConstructorFunction
{
    public GDayConstructorFunction() : base("gDay") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGDay existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGDay(FormatGDay(d.Date.Day, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGDay(FormatGDay(dt.Value.Day, dt.HasTimezone ? dt.Value.Offset : null)));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGDay(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gDay value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGDay(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gDay lexical form: ---DD(timezone)?</summary>
    private static bool ValidateGDay(string s)
    {
        if (s.Length < 5) return false;
        if (s[0] != '-' || s[1] != '-' || s[2] != '-') return false;
        if (s[3] < '0' || s[3] > '9' || s[4] < '0' || s[4] > '9') return false;
        int day = (s[3] - '0') * 10 + (s[4] - '0');
        if (day < 1 || day > 31) return false;
        if (s.Length == 5) return true;
        return GYearConstructorFunction.ValidateTimezone(s, 5);
    }

    private static string FormatGDay(int day, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(12);
        sb.Append("---");
        sb.Append(day.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}

// ──────────────────────────────────────────────
// Binary type constructors
// ──────────────────────────────────────────────
