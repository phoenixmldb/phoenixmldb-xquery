using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// xs:dateTimeStamp requires a timezone. `cast as` and `castable as` enforce it, not only the
/// xs:dateTimeStamp() constructor function.
/// </summary>
public sealed class DateTimeStampCastTests
{
    private static async Task<List<object?>> RunAsync(string query)
    {
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine();
        var compiled = engine.Compile(query);
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return items;
    }

    [Theory]
    [InlineData("'2020-01-01T00:00:00' castable as xs:dateTimeStamp", false)]
    [InlineData("xs:dateTime('2020-01-01T00:00:00') castable as xs:dateTimeStamp", false)]
    [InlineData("'2020-01-01T00:00:00Z' castable as xs:dateTimeStamp", true)]
    [InlineData("xs:dateTime('2020-01-01T00:00:00+02:00') castable as xs:dateTimeStamp", true)]
    public async Task Castable_requires_a_timezone(string query, bool expected)
        => (await RunAsync(query)).Should().Equal(expected);

    [Fact]
    public async Task Cast_without_a_timezone_is_FORG0001()
        => (await FluentActions.Awaiting(() => RunAsync("xs:dateTime('2020-01-01T00:00:00') cast as xs:dateTimeStamp"))
                .Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>())
            .Which.ErrorCode.Should().Be("FORG0001");
}
