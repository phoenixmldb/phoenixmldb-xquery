using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A prefixed QName has no whitespace inside it (XQuery 3.1 A.2.4). The parser skipped whitespace
/// between the prefix, the colon and the local name, so in a map constructor
/// <c>map { $a : fn:abs(2) }</c> the key was read as the variable <c>$a:fn</c> (QT3 UseCaseJSON-003,
/// UseCaseR31-025, fn-load-xquery-module-034).
/// </summary>
public class QNameWhitespaceTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("let $a := 1 return map { $a : fn:abs(-2) }?1", "2")]
    [InlineData("let $a := 1 return map { $a :\n fn:abs(-2) }?1", "2")]
    [InlineData("let $qn-a := 'k' return map { $qn-a : fn:string(5) }?k", "5")]
    public async Task SpacedColonInAMap_SeparatesKeyAndValue(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Theory]
    [InlineData("fn:abs(-1)", "1")]
    [InlineData("map{'x': fn:abs(-3)}?x", "3")]
    public async Task AdjacentPrefixedNames_StillParse(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Fact]
    public async Task WhitespaceInsideAQName_IsASyntaxError()
    {
        var act = () => _facade.EvaluateAsync("fn : abs(-1)");
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("XPST0003");
    }
}
