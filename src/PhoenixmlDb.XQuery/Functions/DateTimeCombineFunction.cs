using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:dateTime($arg1 as xs:date?, $arg2 as xs:time?) as xs:dateTime?</summary>
public sealed class DateTimeCombineFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalItem },
         new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var dateArg = arguments[0];
        var timeArg = arguments[1];

        if (dateArg is null || timeArg is null)
            return ValueTask.FromResult<object?>(null);

        // Parse date component
        DateOnly date;
        TimeSpan? dateTz;
        if (dateArg is Xdm.XsDate xd) { date = xd.Date; dateTz = xd.Timezone; }
        else if (dateArg is DateOnly d) { date = d; dateTz = null; }
        else if (dateArg is DateTimeOffset dto) { date = DateOnly.FromDateTime(dto.DateTime); dateTz = dto.Offset; }
        else { var s = dateArg.ToString()!; var pd = Xdm.XsDate.Parse(s); date = pd.Date; dateTz = pd.Timezone; }

        // Parse time component
        TimeOnly time;
        TimeSpan? timeTz;
        int fracTicks;
        if (timeArg is Xdm.XsTime xt) { time = xt.Time; timeTz = xt.Timezone; fracTicks = xt.FractionalTicks; }
        else if (timeArg is TimeOnly t) { time = t; timeTz = null; fracTicks = (int)(t.Ticks % TimeSpan.TicksPerSecond); }
        else if (timeArg is DateTimeOffset dto2) { time = TimeOnly.FromDateTime(dto2.DateTime); timeTz = dto2.Offset; fracTicks = (int)(dto2.Ticks % TimeSpan.TicksPerSecond); }
        else { var s = timeArg.ToString()!; var pt = Xdm.XsTime.Parse(s); time = pt.Time; timeTz = pt.Timezone; fracTicks = pt.FractionalTicks; }

        // Determine timezone
        bool hasTz;
        TimeSpan offset;
        if (dateTz.HasValue && timeTz.HasValue)
        {
            if (dateTz.Value != timeTz.Value)
                throw context.Error("FORG0008", "dateTime() date and time timezone components are inconsistent");
            offset = dateTz.Value;
            hasTz = true;
        }
        else if (dateTz.HasValue)
        { offset = dateTz.Value; hasTz = true; }
        else if (timeTz.HasValue)
        { offset = timeTz.Value; hasTz = true; }
        else
        { offset = TimeSpan.Zero; hasTz = false; }

        var result = new DateTimeOffset(date.Year, date.Month, date.Day,
            time.Hour, time.Minute, time.Second, offset);
        // Add fractional seconds
        if (fracTicks > 0)
            result = result.AddTicks(fracTicks);
        return ValueTask.FromResult<object?>(new Xdm.XsDateTime(result, hasTz));
    }

    private static DateOnly ParseDateOnly(string s, Ast.ExecutionContext? context = null)
    {
        // Handle xs:date format: YYYY-MM-DD with optional timezone
        var dateStr = s;
        // Strip timezone suffix for DateOnly parsing
        if (dateStr.Length > 10)
        {
            var tzPart = dateStr[10..];
            if (tzPart.StartsWith('+') || tzPart.StartsWith('-') || tzPart.StartsWith('Z'))
                dateStr = dateStr[..10];
        }
        return DateOnly.Parse(dateStr, CultureInfo.InvariantCulture);
    }

    private static TimeOnly ParseTimeOnly(string s, Ast.ExecutionContext? context = null)
    {
        // Handle xs:time format: HH:MM:SS with optional timezone
        var timeStr = s;
        // Strip timezone suffix
        var tzIdx = timeStr.IndexOf('+', 8);
        if (tzIdx < 0) tzIdx = timeStr.IndexOf('-', 8);
        if (tzIdx < 0 && timeStr.EndsWith('Z')) tzIdx = timeStr.Length - 1;
        if (tzIdx >= 0) timeStr = timeStr[..tzIdx];
        return TimeOnly.Parse(timeStr, CultureInfo.InvariantCulture);
    }

    private static bool HasTimezone(object? arg) => arg switch
    {
        DateTimeOffset => true,
        string s => s.EndsWith('Z') || s.Length > 8 && (s.Contains('+') || s.LastIndexOf('-') > s.IndexOf('T')),
        _ => false
    };

    private static TimeSpan GetTimezone(object? arg) => arg switch
    {
        DateTimeOffset dto => dto.Offset,
        string s when s.EndsWith('Z') => TimeSpan.Zero,
        string s => ParseTimezoneFromString(s),
        _ => TimeSpan.Zero
    };

    private static TimeSpan ParseTimezoneFromString(string s, Ast.ExecutionContext? context = null)
    {
        // Try to parse timezone from end of string
        var plusIdx = s.LastIndexOf('+');
        var minusIdx = s.LastIndexOf('-');
        var tzIdx = Math.Max(plusIdx, minusIdx);
        if (tzIdx > 0 && tzIdx > s.IndexOf('T'))
        {
            var tzStr = s[tzIdx..];
            if (TimeSpan.TryParse(tzStr.TrimStart('+'), CultureInfo.InvariantCulture, out var ts))
                return ts;
        }
        return TimeSpan.Zero;
    }
}
