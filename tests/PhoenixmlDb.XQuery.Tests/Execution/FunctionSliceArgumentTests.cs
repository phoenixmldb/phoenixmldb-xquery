using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A declared function walking a string with substring($s, 2) receives a slice of the string,
/// not a copy of its rest, where its parameter is untyped or exactly xs:string (xquery#296), so
/// the walk, a tail recursion, is linear. The parameter is an ordinary string to everything else.
/// </summary>
[Collection(PhoenixmlDb.XQuery.Tests.TimingSensitiveTests.Name)]
public class FunctionSliceArgumentTests
{
    private static Task<string> Eval(string q) => new XQueryFacade().EvaluateAsync(q, inputXml: null);

    [Theory]
    [InlineData("$s")]
    [InlineData("$s as xs:string")]
    public async Task A_string_walk_is_linear(string parameter)
    {
        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var count = await Eval($"declare function local:n({parameter}, $k as xs:integer) as xs:integer {{ if (string-length($s) = 0) then $k else local:n(substring($s, 2), $k + 1) }}; local:n(string-join((1 to 50000) ! 'x'), 0)");
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        count.Should().Be("50000");
        // A copy of the rest at each call is 50,000² / 2 chars = 2.5 GB.
        allocated.Should().BeLessThan(500L * 1024 * 1024);
    }

    [Fact]
    public async Task A_parameter_holding_a_slice_is_an_ordinary_string()
        => (await Eval("""
                declare function local:use($s as xs:string) {
                  string-join(($s instance of xs:string, $s eq 'bcd', concat($s, '!'), upper-case($s), map{$s: 1}?bcd) ! string(), ' ')
                };
                local:use(substring('abcd', 2))
                """)).Should().Be("true true bcd! BCD 1");
}
