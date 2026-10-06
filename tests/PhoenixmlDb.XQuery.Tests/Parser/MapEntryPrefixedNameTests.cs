using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// A map entry whose key or value begins with a prefixed name. The parser splits such an entry at
/// the QName's colon as well as at the separator, and the pieces were stitched back together by a
/// routine that knew a few shapes — a bare name, a function call — and for every other one kept
/// only the prefix: <c>map { 1: fn:true#0 }</c> held the path step <c>fn</c>, and
/// <c>map { 1: xs:integer('3') + 1 }</c> the step <c>xs</c>, with no error. The key and value are
/// now parsed again from their own text.
/// </summary>
public sealed class MapEntryPrefixedNameTests
{
    private static async Task<string> Run(string query) => (await new XQueryFacade().EvaluateAsync(query)).Trim();

    [Theory]
    [InlineData("map { 1: fn:true#0 }(1)()", "true")]
    [InlineData("map { 'a': math:pi#0 }('a')() > 3", "true")]
    [InlineData("map { 1: fn:concat#2 }(1)('a', 'b')", "ab")]
    [InlineData("map { 1: fn:concat#2('a', ?) }(1)('b')", "ab")]
    [InlineData("map { 1: fn:true#0 ! .() }(1)", "true")]
    [InlineData("map { 1: xs:integer('3') + 1 }(1)", "4")]
    [InlineData("map { 'k': if (fn:true()) then xs:int(1) else xs:int(2) }('k')", "1")]
    [InlineData("map { xs:integer('3') + 1: xs:integer('5') * 2 }(4)", "10")]
    [InlineData("map { 'a': map { 1: fn:string-length#1 } }('a')(1)('xyz')", "3")]
    [InlineData("map { 'k': fn:true(), 'j': fn:false() }('j')", "false")]
    [InlineData("map { 1: (fn:true#0) }(1)()", "true")]
    public async Task Value_or_key_beginning_with_a_prefixed_name(string query, string expected)
        => (await Run(query)).Should().Be(expected);

    [Fact]
    public async Task An_entry_that_is_only_a_QName_still_has_no_separator()
    {
        var act = () => Run("map { a:b }");
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("XPST0003");
    }

    [Fact]
    public async Task Error_positions_inside_a_reparsed_value_are_those_of_the_query()
    {
        // The value is on line 3; the re-parse must not report it as line 1.
        var act = () => Run("map {\n  1:\n    fn:no-such-function#0 }");
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("no-such-function");
    }
}
