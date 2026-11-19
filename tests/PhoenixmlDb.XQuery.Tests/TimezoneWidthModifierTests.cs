using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The width modifier on a "z" timezone with no presentation modifier. F&amp;O 3.1 §9.8.4.6 does not
/// say what it means; the expected values are Saxon-HE 12.10's: a minimum width of 1 (or "*" before
/// a maximum) gives one-digit hours, and a maximum width of 2 or less leaves out ":00". The width
/// was ignored, so [z,2-2] gave GMT-05:00 where Saxon and W3C format-date-017 give GMT-05.
/// </summary>
public sealed class TimezoneWidthModifierTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("[z]", "GMT-05:00 GMT+05:30 GMT+00:00 GMT+11:00")]
    [InlineData("[z,1]", "GMT-5:00 GMT+5:30 GMT+0:00 GMT+11:00")]
    [InlineData("[z,1-1]", "GMT-5 GMT+5:30 GMT+0 GMT+11")]
    [InlineData("[z,2]", "GMT-05:00 GMT+05:30 GMT+00:00 GMT+11:00")]
    [InlineData("[z,2-2]", "GMT-05 GMT+05:30 GMT+00 GMT+11")]
    [InlineData("[z,3]", "GMT-05:00 GMT+05:30 GMT+00:00 GMT+11:00")]
    [InlineData("[z,*-2]", "GMT-5 GMT+5:30 GMT+0 GMT+11")]
    [InlineData("[z,4-6]", "GMT-05:00 GMT+05:30 GMT+00:00 GMT+11:00")]
    [InlineData("[z,1-2]", "GMT-5 GMT+5:30 GMT+0 GMT+11")]
    [InlineData("[z0,2-2]", "GMT-5 GMT+5:30 GMT+0 GMT+11")]
    [InlineData("[z00,2]", "GMT-05 GMT+05:30 GMT+00 GMT+11")]
    public async Task The_width_of_a_z_timezone_is_as_in_Saxon(string picture, string expected)
        => (await _facade.EvaluateAsync($"""
                string-join(for $o in ('-05:00', '+05:30', 'Z', '+11:00')
                  return format-dateTime(xs:dateTime('2020-01-01T10:00:00' || $o), '{picture}'), ' ')
                """))
            .Should().Be(expected);
}
