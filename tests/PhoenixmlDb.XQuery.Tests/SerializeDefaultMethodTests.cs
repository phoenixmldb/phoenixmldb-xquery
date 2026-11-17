using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:serialize with no parameters, or with a map that names no method, uses the xml output
/// method: F&amp;O 3.1 §14.1.2 fixes that default for the map form and leaves the one-argument
/// form to the implementation, where Saxon also uses xml. Both used the adaptive method.
/// Expected values are Saxon-HE 12.10's.
/// </summary>
public sealed class SerializeDefaultMethodTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("""serialize((true(), 1, "a"))""", "true 1 a")]
    [InlineData("""serialize((true(), "a"), map{})""", "true a")]
    [InlineData("""serialize((<a/>, "t", <b/>))""", "<a/>t<b/>")]
    [InlineData("""serialize(xs:date("2020-01-01"))""", "2020-01-01")]
    [InlineData("""serialize([1, 2])""", "1 2")]
    [InlineData("""serialize(("a", [1, 2], "b"))""", "a 1 2 b")]
    [InlineData("""serialize(([<x/>], "a"))""", "<x/>a")]
    [InlineData("""serialize([1, 2], map{"item-separator": "|"})""", "1|2")]
    public async Task Uses_the_xml_method_by_default(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Fact]
    public async Task A_map_cannot_be_serialized_with_the_default_method()
    {
        var act = () => _facade.EvaluateAsync("""serialize(map{"k": 1})""");
        var error = (await act.Should().ThrowAsync<Exception>()).Which;
        ((error as PhoenixmlDb.XQuery.Execution.XQueryRuntimeException)?.ErrorCode ?? error.Message).Should().Contain("SENR0001");
    }

    /// <summary>
    /// The xml method failed on an element whose recorded default namespace differs from its
    /// own (an inherited xmlns="urn:d" on a no-namespace element): XmlWriter threw instead of
    /// the element getting xmlns="".
    /// </summary>
    [Fact]
    public async Task An_unprefixed_element_keeps_its_own_default_namespace()
        => (await _facade.EvaluateAsync("""
                let $a := <a:e xmlns:a="urn:a"><c/></a:e>
                return serialize(<r xmlns="urn:d">{$a}</r>, map{"method": "xml"})
                """)).Should().Contain("""<c xmlns=""/>""");
}
