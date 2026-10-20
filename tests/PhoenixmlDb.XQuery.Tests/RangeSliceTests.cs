using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:count of a range, and fn:subsequence of a range, are computed from the range's bounds:
/// count(subsequence(1 to 3000000000, 2147483648)) generated the integers and stopped at the
/// materialization limit (QT3 cbcl-subsequence-010..014). Small ranges check that the slice is
/// the one the function itself selects.
/// </summary>
public class RangeSliceTests
{
    private static Task<string> Eval(string q) => new XQueryFacade().EvaluateAsync(q, inputXml: null);

    [Theory]
    [InlineData("count(subsequence(1 to 3000000000, -2147483648, 2147483647))", "0")]
    [InlineData("count(subsequence(1 to 3000000000, -2147483649))", "3000000000")]
    [InlineData("count(subsequence(1 to 3000000000, 2147483648))", "852516353")]
    [InlineData("string-join(subsequence(1 to 3000000000, 2147483647, 3), ' ')", "2147483647 2147483648 2147483649")]
    public async Task A_large_range_is_sliced_without_generating_it(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Theory]
    [InlineData("subsequence(1 to 10, 2.5, 3)", "3 4 5")]
    [InlineData("subsequence(3 to 10, -1, 4)", "3 4")]
    [InlineData("subsequence(1 to 5, 2, xs:double('INF'))", "2 3 4 5")]
    [InlineData("subsequence(1 to 5, xs:double('NaN'))", "")]
    [InlineData("subsequence(1 to 5, xs:double('-INF'))", "1 2 3 4 5")]
    [InlineData("subsequence(1 to 5, xs:double('-INF'), xs:double('INF'))", "")]
    [InlineData("subsequence(10 to 1, 1)", "")]
    [InlineData("subsequence(1 to 5, xs:untypedAtomic('4'))", "4 5")]
    public async Task A_small_range_gives_the_slice_of_the_function(string query, string expected)
        => (await Eval("string-join(" + query + ", ' ')")).Should().Be(expected);
}
