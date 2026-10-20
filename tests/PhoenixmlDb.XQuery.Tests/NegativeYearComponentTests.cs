using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A year outside 1..9999 reaches fn:dateTime and the component functions. fn:dateTime dropped
/// the year it held apart from the .NET date, so -0012-12-03 combined into 0004-12-03; and an
/// untyped argument, or a node, was parsed as a .NET date, which refused '-0012-12-03'
/// (W3C XSLT type-functions-0101, type-functions-0401).
/// </summary>
public class NegativeYearComponentTests
{
    private static Task<string> Eval(string q) => new XQueryFacade().EvaluateAsync(q, inputXml: null);

    [Fact]
    public async Task DateTime_keeps_a_negative_year()
        => (await Eval("string(dateTime(xs:date('-0012-12-03-05:00'), xs:time('13:20:00')))"))
            .Should().Be("-0012-12-03T13:20:00-05:00");

    [Theory]
    [InlineData("year-from-date(xs:untypedAtomic('-0012-12-03-05:00'))", "-12")]
    [InlineData("month-from-date(<e>-0012-12-03</e>)", "12")]
    [InlineData("day-from-date(<e>-0012-12-03</e>)", "3")]
    [InlineData("year-from-dateTime(<e>-0012-12-03T01:00:00</e>)", "-12")]
    [InlineData("year-from-date(validate type xs:date { <e>-0012-12-03</e> })", "-12")]
    [InlineData("minutes-from-time(<e>13:20:00</e>)", "20")]
    public async Task Component_functions_cast_an_untyped_argument(string query, string expected)
        => (await Eval("string(" + query + ")")).Should().Be(expected);
}
