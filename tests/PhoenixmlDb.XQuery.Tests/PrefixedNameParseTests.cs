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
    public void Prefixed_calls_compile_as_fast_as_unprefixed_calls()
    {
        // The same 10,000 calls with and without the prefix, measured in turn on the same
        // engine: machine speed, JIT tiering and GC affect both alike, so their ratio does not
        // depend on the runner. A ratio of two input sizes did: Windows CI measured 10.4x
        // between 5,000 and 20,000 calls on the fixed code, against 4.4-5.4x on Linux.
        // 10,000 calls: 1.0-1.4x after the fix, 20-23x before it (2.8 s against 0.13 s).
        // The best of five takes out a GC pause.
        var engine = new QueryEngine();
        double Ms(string query)
        {
            var watch = Stopwatch.StartNew();
            var compiled = engine.Compile(query);
            watch.Stop();
            compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
            return watch.Elapsed.TotalMilliseconds;
        }
        static string Calls(string call, int n) => "(" + string.Join(", ", Enumerable.Repeat(call, n)) + ")";

        Ms(Calls("fn:abs(1)", 500)); // warm up
        Ms(Calls("abs(1)", 500));
        var prefixedQuery = Calls("fn:abs(1)", 10_000);
        var unprefixedQuery = Calls("abs(1)", 10_000);
        double prefixed = double.MaxValue, unprefixed = double.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            unprefixed = Math.Min(unprefixed, Ms(unprefixedQuery));
            prefixed = Math.Min(prefixed, Ms(prefixedQuery));
        }
        (prefixed / unprefixed).Should().BeLessThan(5,
            $"10,000 prefixed calls took {prefixed:F0} ms and 10,000 unprefixed calls took {unprefixed:F0} ms");
    }
}
