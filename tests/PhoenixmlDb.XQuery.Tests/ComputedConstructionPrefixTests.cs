using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Regression tests: computed element/attribute construction from a QName with an
/// empty (absent) prefix must be allowed. An empty prefix denotes "no prefix" and is
/// valid; previously the XQDY0074 NCName check rejected the zero-length prefix,
/// breaking <c>attribute {xs:QName('att1')} {...}</c> (and ~49 functx app tests).
/// </summary>
public class ComputedConstructionPrefixTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task Computed_attribute_from_no_prefix_QName_is_allowed()
    {
        var result = await _facade.EvaluateAsync(
            "element a { attribute {xs:QName('att1')} {1}, 'x' }", "<x/>");

        result.Should().Contain("att1=\"1\"");
        result.Should().Contain(">x<");
    }

    [Fact]
    public async Task Computed_element_from_no_prefix_QName_is_allowed()
    {
        var result = await _facade.EvaluateAsync(
            "element {xs:QName('foo')} { 'hi' }", "<x/>");

        result.Should().Contain("<foo>hi</foo>");
    }
}
