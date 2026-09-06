using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:dateTime($arg)</summary>
public sealed class DateTimeConstructorFunction : TypeConstructorFunction
{
    public DateTimeConstructorFunction() : base("dateTime") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is XsDateTime xdt) return ValueTask.FromResult<object?>(xdt);
        // xs:date → xs:dateTime: add T00:00:00 time component
        if (arg is XsDate xd)
        {
            var dto = new DateTimeOffset(xd.Date.ToDateTime(TimeOnly.MinValue),
                xd.Timezone ?? TimeSpan.Zero);
            return ValueTask.FromResult<object?>(new XsDateTime(dto, HasTimezone: xd.Timezone.HasValue));
        }
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        // Validate leading + (not allowed) and leading zeros in >4 digit year
        ValidateDateYearPrefix(s, "xs:dateTime");
        ValidateDateTimeLexical(s);
        try { return ValueTask.FromResult<object?>(XsDateTime.Parse(s)); }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dateTime: {ex.Message}"); }
    }

    /// <summary>Validates that a date/dateTime string doesn't have a leading '+' or leading zeros in >4 digit year.</summary>
    internal static void ValidateDateYearPrefix(string s, string typeName)
    {
        if (s.Length == 0) return;
        int i = 0;
        if (s[i] == '+')
            throw new XQueryRuntimeException("FORG0001", $"Leading '+' is not allowed for {typeName}: '{s}'");
        if (s[i] == '-') i++;
        // Count year digits
        int digitStart = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        int digitCount = i - digitStart;
        // If >4 digits, leading zeros are prohibited
        if (digitCount > 4 && s[digitStart] == '0')
            throw new XQueryRuntimeException("FORG0001", $"Leading zeros in year with more than 4 digits not allowed for {typeName}: '{s}'");
    }
}
