using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// xs:dateTimeStamp is a type derived from xs:dateTime, not every xs:dateTime that has a timezone.
/// A value is an xs:dateTimeStamp when the xs:dateTimeStamp constructor, a cast to it or
/// fn:current-dateTime() gives it (Core's XsDateTime.IsDateTimeStamp); a cast to xs:dateTime and an
/// operation that makes a new value give an xs:dateTime. The two are still equal. An xs:dateTime
/// with a timezone matched xs:dateTimeStamp: xs:dateTime('2026-01-01T00:00:00Z') instance of
/// xs:dateTimeStamp was true. Every expected value is Saxon-HE 12.10's.
/// </summary>
public sealed class DateTimeStampTypeTests
{
    private const string T = "'2026-01-01T00:00:00Z'";

    private static async Task<List<object?>> RunAsync(string query)
    {
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine();
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => $"{e.Code} {e.Message}")));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return items;
    }

    [Theory]
    [InlineData($"xs:dateTime({T}) instance of xs:dateTimeStamp", false)]
    [InlineData($"xs:dateTimeStamp({T}) instance of xs:dateTimeStamp", true)]
    [InlineData($"xs:dateTimeStamp({T}) instance of xs:dateTime", true)]
    [InlineData($"(xs:dateTime({T}) cast as xs:dateTimeStamp) instance of xs:dateTimeStamp", true)]
    [InlineData($"({T} cast as xs:dateTimeStamp) instance of xs:dateTimeStamp", true)]
    [InlineData($"(xs:dateTimeStamp({T}) cast as xs:dateTime) instance of xs:dateTimeStamp", false)]
    [InlineData($"(xs:dateTimeStamp({T}) + xs:dayTimeDuration('PT1H')) instance of xs:dateTimeStamp", false)]
    [InlineData($"adjust-dateTime-to-timezone(xs:dateTimeStamp({T}), xs:dayTimeDuration('PT1H')) instance of xs:dateTimeStamp", false)]
    [InlineData($"xs:dateTimeStamp({T}) eq xs:dateTime({T})", true)]
    [InlineData($"max(xs:dateTimeStamp({T})) instance of xs:dateTimeStamp", true)]
    [InlineData($"(xs:dateTimeStamp({T}), xs:dateTime({T}))[1] instance of xs:dateTimeStamp", true)]
    [InlineData("current-dateTime() instance of xs:dateTimeStamp", true)]
    public async Task The_type_of_a_value(string query, bool expected)
        => (await RunAsync(query)).Should().Equal(expected);

    [Theory]
    [InlineData($"typeswitch (xs:dateTime({T})) case xs:dateTimeStamp return 'stamp' default return 'dt'", "dt")]
    [InlineData($"typeswitch (xs:dateTimeStamp({T})) case xs:dateTimeStamp return 'stamp' default return 'dt'", "stamp")]
    public async Task Typeswitch_tells_the_types_apart(string query, string expected)
        => (await RunAsync(query)).Should().Equal(expected);

    [Fact]
    public async Task An_xs_dateTimeStamp_and_the_equal_xs_dateTime_are_one_distinct_value()
        => (await RunAsync($"count(distinct-values((xs:dateTimeStamp({T}), xs:dateTime({T}))))")).Should().Equal(1L);

    [Fact]
    public async Task Treat_as_xs_dateTimeStamp_of_an_xs_dateTime_is_XPDY0050()
        => (await FluentActions.Awaiting(() => RunAsync($"xs:dateTime({T}) treat as xs:dateTimeStamp"))
                .Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>())
            .Which.ErrorCode.Should().Be("XPDY0050");
}
