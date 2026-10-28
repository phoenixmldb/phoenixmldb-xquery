using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// An xs:string parameter of a built-in accepts a string, an xs:untypedAtomic or an xs:anyURI;
/// any other atomic value is XPTY0004. Numbers and booleans were refused, but a date, time,
/// duration, g* value or QName was taken by its string form (QT3 fn-normalize-space-26).
/// </summary>
public class StringParameterAtomicTests
{
    private static async Task<string> Eval(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("normalize-space(xs:date('2000-01-01'))")]
    [InlineData("current-date()[normalize-space(.)]")]
    [InlineData("upper-case(xs:dateTime('2000-01-01T00:00:00'))")]
    [InlineData("string-length(xs:time('12:00:00'))")]
    [InlineData("contains(xs:dayTimeDuration('PT1H'), 'H')")]
    [InlineData("contains(xs:yearMonthDuration('P1Y'), 'Y')")]
    [InlineData("contains(xs:duration('P1Y1D'), 'Y')")]
    [InlineData("lower-case(xs:gYear('2000'))")]
    [InlineData("lower-case(xs:QName('a'))")]
    public async Task Other_atomic_types_are_XPTY0004(string query)
    {
        var act = () => Eval(query);
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("must be xs:string");
    }

    [Theory]
    [InlineData("normalize-space(xs:untypedAtomic(' a  b '))", "a b")]
    [InlineData("normalize-space(xs:anyURI('a'))", "a")]
    [InlineData("normalize-space(xs:token('a'))", "a")]
    [InlineData("upper-case(string(xs:date('2000-01-01')))", "2000-01-01")]
    public async Task Strings_untyped_and_anyURI_are_accepted(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
