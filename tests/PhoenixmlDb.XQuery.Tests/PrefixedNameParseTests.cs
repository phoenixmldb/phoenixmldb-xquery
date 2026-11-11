using System.Diagnostics;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The colon of a prefixed name is a token of its own (QNAME_COLON), given by the token stream
/// when a name is directly on each side of it. The parser told <c>fn:abs</c> from a name followed
/// by a map separator with a predicate on the adjacency, which ANTLR evaluated only at the end of
/// the expression: each prefixed name looked ahead to the end of the query, and 10,000 calls of
/// <c>fn:abs(1)</c> (107 KB) took 2.8 s to compile (xquery#363). Each case must read as before.
/// </summary>
public class PrefixedNameParseTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("fn:abs(-1)", "1")]
    [InlineData("map{'a':fn:abs(-2)}?a", "2")]
    [InlineData("map{xs:string('k'):fn:abs(-3)}?k", "3")]
    [InlineData("map{1:2}(1)", "2")]
    [InlineData("let $a := 1 return map { $a : fn:abs(-2) }?1", "2")]
    // a:b:c is the QName a:b, then the separator: the colon after a local part is not a QName's.
    [InlineData("declare namespace a='u'; let $d := <r xmlns:a='u'><a:b>k</a:b></r> return map{$d/a:b:3}?k", "3")]
    // QName, separator, QName (QT3 MapConstructor-026); a wildcard's local part is followed by the separator.
    [InlineData("declare namespace z='http://z.com'; map:size(<a><z:b>x</z:b></a>/map{z:b:z:b})", "1")]
    [InlineData("declare namespace z='http://z.com'; (<a><z:b>x</z:b></a>/map{z:b:z:b})?x/string()", "x")]
    [InlineData("map:size(<a><b>x</b></a>/map{*:b:1})", "1")]
    [InlineData("declare namespace p='u'; declare variable $p:v := 5; $p:v", "5")]
    [InlineData("declare namespace p='u'; count(<a><p:b xmlns:p='u'/><c/></a>/p:*)", "1")]
    [InlineData("count(<a><b/><c/></a>/*:b)", "1")]
    [InlineData("declare namespace p='u'; declare function p:f($x) { $x * 2 }; p:f(21)", "42")]
    [InlineData("xs:integer('7') + fn:count((1, 2))", "9")]
    [InlineData("record{a:1}?a", "1")]
    public async Task Prefixed_names_read_as_before(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Theory]
    [InlineData("fn : abs(-1)")]
    [InlineData("fn: abs(-1)")]
    [InlineData("fn :abs(-1)")]
    [InlineData("fn(::):abs(-1)")]
    // A QName takes the adjacent colon, so the entry has no separator.
    [InlineData("map{a:b}")]
    public async Task A_colon_that_is_not_adjacent_to_both_names_makes_no_QName(string query)
    {
        var act = () => _facade.EvaluateAsync(query);
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("XPST0003");
    }
}

/// <summary>Compile time of prefixed names, measured alone (xquery#363).</summary>
[Collection(TimingSensitiveTests.Name)]
public sealed class PrefixedNameParseTimeTests
{
    [Fact]
    public void Compile_time_of_prefixed_calls_is_linear()
    {
        // 2,500 calls: about 0.2 s before the fix; 10,000: 2.8 s. Compared by the ratio of two
        // sizes, not against a clock: four times the input is about four times the time when
        // linear and sixteen when quadratic. The best of three takes out a GC pause.
        var engine = new QueryEngine();
        double BestMs(int n)
        {
            var query = "(" + string.Join(", ", Enumerable.Repeat("fn:abs(1)", n)) + ")";
            var best = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                var watch = Stopwatch.StartNew();
                var compiled = engine.Compile(query);
                watch.Stop();
                compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        BestMs(500); // warm up
        var small = BestMs(2_500);
        var large = BestMs(10_000);
        (large / small).Should().BeLessThan(9, $"2,500 calls took {small:F0} ms and 10,000 took {large:F0} ms");
    }
}
