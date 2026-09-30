using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Calling a map or array as a function returns its value, and an empty value is the empty
/// sequence. It is stored as null, and the dynamic-call path yielded that null as an item, so
/// $m('k') counted one item where map:get($m, 'k') and $m?k count none (W3C XSLT si-map-005,
/// reported by the engine session on Xslt 2.4.1).
/// </summary>
public class MapArrayCallEmptyValueTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("let $m := map { 'k': () } return count($m('k'))", "0")]
    [InlineData("count(map { 'k': () }('k'))", "0")]
    [InlineData("let $m := map { 'k': () } return empty($m('k'))", "true")]
    [InlineData("let $a := [(), 1] return count($a(1))", "0")]
    [InlineData("let $a := [(), 1] return count($a(2))", "1")]
    [InlineData("let $m := map { 'k': (1, 2) } return count($m('k'))", "2")]
    public async Task An_empty_value_is_the_empty_sequence(string query, string expected) =>
        (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
