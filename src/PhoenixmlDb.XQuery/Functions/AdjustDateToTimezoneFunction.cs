using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:adjust-date-to-timezone($arg as xs:date?) as xs:date?</summary>
public sealed class AdjustDateToTimezoneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-date-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var implicitTz = DateTimeOffset.Now.Offset;
        return ValueTask.FromResult<object?>(AdjustDate(arg, implicitTz));
    }

    internal static object? AdjustDate(object? arg, TimeSpan? newTz, Ast.ExecutionContext? context = null)
    {
        if (arg is Xdm.XsDate xd)
        {
            if (newTz is null)
                return new Xdm.XsDate(xd.Date, null) { ExtendedYear = xd.ExtendedYear };
            if (xd.Timezone is null)
                return new Xdm.XsDate(xd.Date, newTz) { ExtendedYear = xd.ExtendedYear };
            // Has timezone → adjust: convert to UTC then to new timezone
            // For dates, the adjustment may change the date if the timezone offset crosses midnight
            var utcDate = xd.Date.ToDateTime(TimeOnly.MinValue) - xd.Timezone.Value;
            var newDate = utcDate + newTz.Value;
            var resultDate = DateOnly.FromDateTime(newDate);
            return new Xdm.XsDate(resultDate, newTz) { ExtendedYear = xd.ExtendedYear };
        }
        // Fallback for DateTimeOffset
        var dt = DateTimeHelper.ParseDateTimeOffset(arg!);
        if (newTz is null)
        {
            var date = DateOnly.FromDateTime(dt.DateTime);
            return new Xdm.XsDate(date, null);
        }
        var adjusted = dt.ToOffset(newTz.Value);
        return new Xdm.XsDate(DateOnly.FromDateTime(adjusted.DateTime), newTz);
    }
}
