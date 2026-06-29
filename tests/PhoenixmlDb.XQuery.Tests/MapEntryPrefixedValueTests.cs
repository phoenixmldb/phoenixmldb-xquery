using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A map-entry VALUE that contains a prefixed function call gets split by ANTLR across the
/// colon (`xs:decimal(...)` → `xs` `:` `decimal(...)`). When the leading call is further
/// wrapped in cast/castable/treat/instance-of, the prefix must reattach to the inner call and
/// keep the wrapper — otherwise the value was silently dropped (returned the bare prefix as a
/// path step → empty). Regression for QT3 Serialization-json-18 / `map{$k:pfx:fn()}`.
/// </summary>
public class MapEntryPrefixedValueTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("string(map{'a': xs:decimal(12.5) cast as xs:float}('a'))", "12.5")]
    [InlineData("string(map{'a': fn:abs(-12.5) cast as xs:float}('a'))", "12.5")]
    [InlineData("map{'a': xs:decimal(12.5) castable as xs:float}('a')", "true")]
    [InlineData("string(map{'a': xs:decimal(12.5) treat as xs:decimal}('a'))", "12.5")]
    [InlineData("map{'a': xs:decimal(12.5) instance of xs:decimal}('a')", "true")]
    // bare prefixed function-call value (no trailing operator) still works
    [InlineData("string(map{'a': xs:string(42)}('a'))", "42")]
    public async Task Prefixed_function_call_value_is_not_dropped(string query, string expected)
    {
        var result = await _facade.EvaluateAsync(query, "<x/>");
        result.Should().Be(expected);
    }

    [Fact]
    public async Task Json_serialization_of_cast_float_map_value()
    {
        // The original QT3 Serialization-json-18 shape.
        var result = await _facade.EvaluateAsync(
            "fn:serialize(map { 'a' : xs:decimal(12678967.543233) cast as xs:float }, map { 'method':'json' })",
            "<x/>");
        result.Should().NotContain("null");
        result.Should().MatchRegex("\\{\"a\":1\\.2678\\d*[Ee]\\+?7\\}");
    }
}
