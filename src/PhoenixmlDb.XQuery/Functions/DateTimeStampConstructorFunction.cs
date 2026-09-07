using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:dateTimeStamp($arg) — xs:dateTime with mandatory timezone</summary>
public sealed class DateTimeStampConstructorFunction : TypeConstructorFunction
{
    public DateTimeStampConstructorFunction() : base("dateTimeStamp") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);

        XsDateTime result;
        if (arg is XsDateTime xdt)
        {
            result = xdt;
        }
        else if (arg is XsDate xd)
        {
            // Cast xs:date → xs:dateTimeStamp: date must have timezone
            if (xd.Timezone is null)
                throw new XQueryRuntimeException("FORG0001",
                    "Cannot cast xs:date without timezone to xs:dateTimeStamp");
            result = new XsDateTime(
                new DateTimeOffset(xd.Date.ToDateTime(TimeOnly.MinValue), xd.Timezone.Value),
                HasTimezone: true);
        }
        else
        {
            var s = arg.ToString()!.Trim();
            try { result = XsDateTime.Parse(s); }
            catch (XQueryRuntimeException) { throw; }
            catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dateTimeStamp: {ex.Message}"); }
        }

        // xs:dateTimeStamp requires a timezone
        if (!result.HasTimezone)
            throw new XQueryRuntimeException("FORG0001",
                "xs:dateTimeStamp requires a timezone component");

        return ValueTask.FromResult<object?>(result);
    }
}
