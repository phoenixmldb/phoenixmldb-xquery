using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

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
    /// Validate a timezone offset per XPath F&amp;O §10.7: must be between -14:00 and +14:00
    /// and must be an integral number of minutes. Raises FODT0003 on violation.
    /// </summary>
    public static void ValidateTimezoneOffset(TimeSpan offset, Ast.ExecutionContext? context = null)
    {
        var maxTz = TimeSpan.FromHours(14);
        if (offset < -maxTz || offset > maxTz)
            throw context.Error("FODT0003",
                $"Timezone offset {offset} is out of range (-14:00 to +14:00)");
        if (offset.Ticks % TimeSpan.TicksPerMinute != 0)
            throw context.Error("FODT0003",
                $"Timezone offset must be an integral number of minutes, got {offset}");
    }

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
