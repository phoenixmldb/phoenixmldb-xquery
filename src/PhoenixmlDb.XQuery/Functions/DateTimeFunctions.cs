using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

// ──────────────────────────────────────────────
// Date component accessor functions
// ──────────────────────────────────────────────

/// <summary>fn:year-from-date($arg as xs:date?) as xs:integer?</summary>
public sealed class YearFromDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "year-from-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = ParseDate(arg);
        return ValueTask.FromResult<object?>((long)dt.Year);
    }

    private static DateTimeOffset ParseDate(object arg) => arg switch
    {
        Xdm.XsDate xd => new DateTimeOffset(xd.Date.Year, xd.Date.Month, xd.Date.Day, 0, 0, 0, xd.Timezone ?? TimeSpan.Zero),
        Xdm.XsDateTime xdt => xdt.Value,
        DateOnly d => new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero),
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
        _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
    };
}

/// <summary>fn:month-from-date($arg as xs:date?) as xs:integer?</summary>
public sealed class MonthFromDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "month-from-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = ParseDate(arg);
        return ValueTask.FromResult<object?>((long)dt.Month);
    }

    private static DateTimeOffset ParseDate(object arg) => arg switch
    {
        Xdm.XsDate xd => new DateTimeOffset(xd.Date.Year, xd.Date.Month, xd.Date.Day, 0, 0, 0, xd.Timezone ?? TimeSpan.Zero),
        Xdm.XsDateTime xdt => xdt.Value,
        DateOnly d => new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero),
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
        _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
    };
}

/// <summary>fn:day-from-date($arg as xs:date?) as xs:integer?</summary>
public sealed class DayFromDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "day-from-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = ParseDate(arg);
        return ValueTask.FromResult<object?>((long)dt.Day);
    }

    private static DateTimeOffset ParseDate(object arg) => arg switch
    {
        Xdm.XsDate xd => new DateTimeOffset(xd.Date.Year, xd.Date.Month, xd.Date.Day, 0, 0, 0, xd.Timezone ?? TimeSpan.Zero),
        Xdm.XsDateTime xdt => xdt.Value,
        DateOnly d => new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero),
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
        _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
    };
}

// ──────────────────────────────────────────────
// DateTime component accessor functions
// ──────────────────────────────────────────────

/// <summary>fn:year-from-dateTime($arg as xs:dateTime?) as xs:integer?</summary>
public sealed class YearFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "year-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = ParseDateTime(arg);
        return ValueTask.FromResult<object?>((long)dt.Year);
    }

    private static DateTimeOffset ParseDateTime(object arg) => arg switch
    {
        Xdm.XsDateTime xdt => xdt.Value,
        Xdm.XsDate xd => new DateTimeOffset(xd.Date.Year, xd.Date.Month, xd.Date.Day, 0, 0, 0, xd.Timezone ?? TimeSpan.Zero),
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
        _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
    };
}

/// <summary>fn:month-from-dateTime($arg as xs:dateTime?) as xs:integer?</summary>
public sealed class MonthFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "month-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = ParseDateTime(arg);
        return ValueTask.FromResult<object?>((long)dt.Month);
    }

    private static DateTimeOffset ParseDateTime(object arg) => arg switch
    {
        Xdm.XsDateTime xdt => xdt.Value,
        Xdm.XsDate xd => new DateTimeOffset(xd.Date.Year, xd.Date.Month, xd.Date.Day, 0, 0, 0, xd.Timezone ?? TimeSpan.Zero),
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
        _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
    };
}

/// <summary>fn:day-from-dateTime($arg as xs:dateTime?) as xs:integer?</summary>
public sealed class DayFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "day-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = ParseDateTime(arg);
        return ValueTask.FromResult<object?>((long)dt.Day);
    }

    private static DateTimeOffset ParseDateTime(object arg) => arg switch
    {
        Xdm.XsDateTime xdt => xdt.Value,
        Xdm.XsDate xd => new DateTimeOffset(xd.Date.Year, xd.Date.Month, xd.Date.Day, 0, 0, 0, xd.Timezone ?? TimeSpan.Zero),
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
        _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
    };
}

/// <summary>fn:hours-from-dateTime($arg as xs:dateTime?) as xs:integer?</summary>
public sealed class HoursFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "hours-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = arg switch
        {
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>((long)dt.Hour);
    }
}

/// <summary>fn:minutes-from-dateTime($arg as xs:dateTime?) as xs:integer?</summary>
public sealed class MinutesFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "minutes-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = arg switch
        {
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>((long)dt.Minute);
    }
}

/// <summary>fn:seconds-from-dateTime($arg as xs:dateTime?) as xs:decimal?</summary>
public sealed class SecondsFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "seconds-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.Decimal;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = arg switch
        {
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>((decimal)dt.Second + (decimal)dt.Millisecond / 1000m);
    }
}

// ──────────────────────────────────────────────
// Time component accessor functions
// ──────────────────────────────────────────────

/// <summary>fn:hours-from-time($arg as xs:time?) as xs:integer?</summary>
public sealed class HoursFromTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "hours-from-time");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = arg switch
        {
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>((long)dt.Hour);
    }
}

/// <summary>fn:minutes-from-time($arg as xs:time?) as xs:integer?</summary>
public sealed class MinutesFromTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "minutes-from-time");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = arg switch
        {
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>((long)dt.Minute);
    }
}

/// <summary>fn:seconds-from-time($arg as xs:time?) as xs:decimal?</summary>
public sealed class SecondsFromTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "seconds-from-time");
    public override XdmSequenceType ReturnType => XdmSequenceType.Decimal;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = arg switch
        {
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>((decimal)dt.Second + (decimal)dt.Millisecond / 1000m);
    }
}

// ──────────────────────────────────────────────
// Duration component accessor functions
// ──────────────────────────────────────────────

/// <summary>fn:years-from-duration($arg as xs:duration?) as xs:integer?</summary>
public sealed class YearsFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "years-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.YearMonthDuration ymd)
            return ValueTask.FromResult<object?>((long)ymd.Years);
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>((long)dur.Years);
        var ts = arg switch
        {
            TimeSpan t => t,
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((long)(ts.Days / 365));
    }
}

/// <summary>fn:months-from-duration($arg as xs:duration?) as xs:integer?</summary>
public sealed class MonthsFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "months-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.YearMonthDuration ymd)
            return ValueTask.FromResult<object?>((long)ymd.Months);
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>((long)dur.Months);
        var ts = arg switch
        {
            TimeSpan t => t,
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((long)((ts.Days % 365) / 30));
    }
}

/// <summary>fn:days-from-duration($arg as xs:duration?) as xs:integer?</summary>
public sealed class DaysFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "days-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>((long)dur.DayTime.Days);
        var ts = arg switch
        {
            TimeSpan t => t,
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((long)ts.Days);
    }
}

/// <summary>fn:hours-from-duration($arg as xs:duration?) as xs:integer?</summary>
public sealed class HoursFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "hours-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>((long)dur.DayTime.Hours);
        var ts = arg switch
        {
            TimeSpan t => t,
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((long)ts.Hours);
    }
}

/// <summary>fn:minutes-from-duration($arg as xs:duration?) as xs:integer?</summary>
public sealed class MinutesFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "minutes-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>((long)dur.DayTime.Minutes);
        var ts = arg switch
        {
            TimeSpan t => t,
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((long)ts.Minutes);
    }
}

/// <summary>fn:seconds-from-duration($arg as xs:duration?) as xs:decimal?</summary>
public sealed class SecondsFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "seconds-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Decimal;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDuration dur)
        {
            var ts = dur.DayTime;
            return ValueTask.FromResult<object?>((decimal)ts.Seconds + (decimal)ts.Milliseconds / 1000m);
        }
        var ts2 = arg switch
        {
            TimeSpan t => t,
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((decimal)ts2.Seconds + (decimal)ts2.Milliseconds / 1000m);
    }
}

// ──────────────────────────────────────────────
// Timezone accessor functions
// ──────────────────────────────────────────────

/// <summary>fn:timezone-from-dateTime($arg as xs:dateTime?) as xs:dayTimeDuration?</summary>
public sealed class TimezoneFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "timezone-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDateTime xdt)
            return ValueTask.FromResult<object?>(xdt.HasTimezone ? (object)xdt.Value.Offset : null);
        if (arg is DateTimeOffset dto)
            return ValueTask.FromResult<object?>((object)dto.Offset);
        var parsed = Xdm.XsDateTime.Parse(arg.ToString()!);
        return ValueTask.FromResult<object?>(parsed.HasTimezone ? (object)parsed.Value.Offset : null);
    }
}

/// <summary>fn:timezone-from-date($arg as xs:date?) as xs:dayTimeDuration?</summary>
public sealed class TimezoneFromDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "timezone-from-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDate xd)
            return ValueTask.FromResult<object?>(xd.Timezone.HasValue ? (object)xd.Timezone.Value : null);
        if (arg is DateOnly) return ValueTask.FromResult<object?>(null);
        if (arg is DateTimeOffset dto)
            return ValueTask.FromResult<object?>((object)dto.Offset);
        var parsed = Xdm.XsDate.Parse(arg.ToString()!);
        return ValueTask.FromResult<object?>(parsed.Timezone.HasValue ? (object)parsed.Timezone.Value : null);
    }
}

/// <summary>fn:timezone-from-time($arg as xs:time?) as xs:dayTimeDuration?</summary>
public sealed class TimezoneFromTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "timezone-from-time");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsTime xt)
            return ValueTask.FromResult<object?>(xt.Timezone.HasValue ? (object)xt.Timezone.Value : null);
        if (arg is DateTimeOffset dto)
            return ValueTask.FromResult<object?>((object)dto.Offset);
        var parsed = Xdm.XsTime.Parse(arg.ToString()!);
        return ValueTask.FromResult<object?>(parsed.Timezone.HasValue ? (object)parsed.Timezone.Value : null);
    }
}

/// <summary>fn:implicit-timezone() as xs:dayTimeDuration</summary>
public sealed class ImplicitTimezoneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "implicit-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>((object)DateTimeOffset.Now.Offset);
    }
}

/// <summary>fn:adjust-dateTime-to-timezone($arg as xs:dateTime?) as xs:dateTime?</summary>
public sealed class AdjustDateTimeToTimezoneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-dateTime-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = DateTimeHelper.ParseDateTimeOffset(arg);
        var adjusted = dt.ToOffset(DateTimeOffset.Now.Offset);
        return ValueTask.FromResult<object?>((object)adjusted);
    }
}

/// <summary>fn:adjust-dateTime-to-timezone($arg, $timezone) as xs:dateTime?</summary>
public sealed class AdjustDateTimeToTimezone2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-dateTime-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem },
         new() { Name = new QName(NamespaceId.None, "timezone"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = DateTimeHelper.ParseDateTimeOffset(arg);
        var tz = arguments[1];
        if (tz is null)
            return ValueTask.FromResult<object?>((object)new DateTimeOffset(dt.DateTime, TimeSpan.Zero));
        var offset = tz is TimeSpan ts ? ts : TimeSpan.Parse(tz.ToString()!);
        return ValueTask.FromResult<object?>((object)dt.ToOffset(offset));
    }
}

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
        var dt = DateTimeHelper.ParseDateTimeOffset(arg);
        return ValueTask.FromResult<object?>((object)dt.ToOffset(DateTimeOffset.Now.Offset));
    }
}

/// <summary>fn:adjust-date-to-timezone($arg, $timezone) as xs:date?</summary>
public sealed class AdjustDateToTimezone2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-date-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem },
         new() { Name = new QName(NamespaceId.None, "timezone"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = DateTimeHelper.ParseDateTimeOffset(arg);
        var tz = arguments[1];
        if (tz is null)
            return ValueTask.FromResult<object?>((object)new DateTimeOffset(dt.DateTime, TimeSpan.Zero));
        var offset = tz is TimeSpan ts ? ts : TimeSpan.Parse(tz.ToString()!);
        return ValueTask.FromResult<object?>((object)dt.ToOffset(offset));
    }
}

/// <summary>fn:adjust-time-to-timezone($arg as xs:time?) as xs:time?</summary>
public sealed class AdjustTimeToTimezoneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-time-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var xt = DateTimeHelper.ParseXsTime(arg);
        var implicitTz = DateTimeOffset.Now.Offset;
        return ValueTask.FromResult<object?>((object)DateTimeHelper.AdjustTimeToTimezone(xt, implicitTz));
    }
}

/// <summary>fn:adjust-time-to-timezone($arg, $timezone) as xs:time?</summary>
public sealed class AdjustTimeToTimezone2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-time-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem },
         new() { Name = new QName(NamespaceId.None, "timezone"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var xt = DateTimeHelper.ParseXsTime(arg);
        var tz = arguments[1];
        if (tz is null)
        {
            // Remove timezone: return time with no timezone
            return ValueTask.FromResult<object?>((object)new Xdm.XsTime(xt.Time, null, xt.FractionalTicks));
        }
        var offset = tz is TimeSpan ts ? ts : TimeSpan.Parse(tz.ToString()!);
        return ValueTask.FromResult<object?>((object)DateTimeHelper.AdjustTimeToTimezone(xt, offset));
    }
}

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
                throw new XQueryException("FORG0008", "dateTime() date and time timezone components are inconsistent");
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

    private static DateOnly ParseDateOnly(string s)
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

    private static TimeOnly ParseTimeOnly(string s)
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

    private static TimeSpan ParseTimezoneFromString(string s)
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

internal static class DateTimeHelper
{
    public static DateTimeOffset ParseDateTimeOffset(object arg) => arg switch
    {
        Xdm.XsDateTime xdt => xdt.Value,
        Xdm.XsDate xd => new DateTimeOffset(xd.Date.Year, xd.Date.Month, xd.Date.Day, 0, 0, 0, xd.Timezone ?? TimeSpan.Zero),
        Xdm.XsTime xt => new DateTimeOffset(1, 1, 2, xt.Time.Hour, xt.Time.Minute, xt.Time.Second, xt.Timezone ?? TimeSpan.Zero),
        DateOnly d => new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero),
        TimeOnly t => new DateTimeOffset(1, 1, 2, t.Hour, t.Minute, t.Second, TimeSpan.Zero),
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
        _ => DateTimeOffset.Parse(arg.ToString()!, System.Globalization.CultureInfo.InvariantCulture)
    };

    /// <summary>Parse an XsTime from any time-like argument.</summary>
    public static Xdm.XsTime ParseXsTime(object arg) => arg switch
    {
        Xdm.XsTime xt => xt,
        TimeOnly t => new Xdm.XsTime(t, null, (int)(t.Ticks % TimeSpan.TicksPerSecond)),
        DateTimeOffset dto => new Xdm.XsTime(TimeOnly.FromDateTime(dto.DateTime), dto.Offset, (int)(dto.Ticks % TimeSpan.TicksPerSecond)),
        string s => Xdm.XsTime.Parse(s),
        _ => Xdm.XsTime.Parse(arg.ToString()!)
    };

    /// <summary>
    /// Adjust an xs:time to a target timezone per XPath F&amp;O §10.7.3.
    /// Uses TimeOnly arithmetic to avoid DateTimeOffset underflow on year 1 boundary.
    /// </summary>
    public static Xdm.XsTime AdjustTimeToTimezone(Xdm.XsTime time, TimeSpan targetTz)
    {
        // If the time has no timezone, just stamp it with the target timezone
        if (!time.Timezone.HasValue)
            return new Xdm.XsTime(time.Time, targetTz, time.FractionalTicks);

        // Convert to UTC then to target timezone using tick arithmetic (wraps at 24h)
        var utcTicks = time.Time.Ticks - time.Timezone.Value.Ticks;
        var targetTicks = utcTicks + targetTz.Ticks;

        // Wrap around 24-hour boundary
        var ticksPerDay = TimeSpan.TicksPerDay;
        targetTicks = ((targetTicks % ticksPerDay) + ticksPerDay) % ticksPerDay;

        var adjustedTime = new TimeOnly(targetTicks);
        return new Xdm.XsTime(adjustedTime, targetTz, time.FractionalTicks);
    }
}
