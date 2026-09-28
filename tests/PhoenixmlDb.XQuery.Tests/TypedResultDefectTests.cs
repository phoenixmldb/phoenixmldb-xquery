using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Engine type defects that the QT3 harness passed by default until #49 made assert-type honest
/// (xquery#83). Each is a value of the right lexical form but the wrong TYPE, or the wrong error
/// code, so nothing looked wrong until something asked.
/// </summary>
public class TypedResultDefectTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    // fn:namespace-uri is xs:anyURI, not xs:string (fn-namespace-uri-13/16/17).
    [InlineData("""fn:namespace-uri(attribute a {"x"}) instance of xs:anyURI""")]
    [InlineData("""namespace-uri(<a xmlns="http://e"/>) instance of xs:anyURI""")]
    [InlineData("""<a xmlns="http://e"/>/namespace-uri() instance of xs:anyURI""")]
    // fn:prefix-from-QName is xs:NCName (fn-prefix-from-qname-21/22).
    [InlineData("""fn:prefix-from-QName(QName("u", "p:a")) instance of xs:NCName""")]
    // fn:default-language is xs:language (default-language-001).
    [InlineData("""default-language() instance of xs:language""")]
    // map:put replaces the KEY too when an equal key of another type is put (map-put-011).
    [InlineData("""map:put(map{3 : "three"}, xs:float("3.0"), "threeF") instance of map(xs:float, xs:string)""")]
    // Casting to the union type xs:numeric keeps a member type, subtype included (xs-numeric-017).
    [InlineData("""(xs:short(256) cast as xs:numeric) instance of xs:short""")]
    public async Task The_result_has_the_specified_type(string query)
        => (await _facade.EvaluateAsync(query)).Should().Be("true");

    /// <summary>The new types still behave as strings where a string is wanted.</summary>
    [Theory]
    [InlineData("""namespace-uri(<a xmlns="http://e"/>) eq "http://e" """)]
    [InlineData("""string-length(namespace-uri(<a xmlns="http://e"/>)) eq 8""")]
    [InlineData("""fn:prefix-from-QName(QName("u", "p:a")) eq "p" """)]
    public async Task The_typed_results_still_compare_as_strings(string query)
        => (await _facade.EvaluateAsync(query)).Should().Be("true");

    /// <summary>
    /// The facade's default method renders a plain xs:string bare; an xs:anyURI must come back the
    /// same way. It was quoted — so once namespace-uri() returned xs:anyURI, every such result
    /// through the facade would have gained quotes.
    /// </summary>
    [Fact]
    public async Task The_facade_default_renders_an_anyURI_bare_like_a_string()
        => (await _facade.EvaluateAsync("""namespace-uri(<a xmlns="urn:x"/>)""")).Should().Be("urn:x");

    [Fact]
    public async Task Map_put_keeps_the_entry_in_its_position()
        => (await _facade.EvaluateAsync("""string-join(map:keys(map:put(map{"a":1,"b":2,"c":3}, "b", 20)), ",")"""))
            .Should().Be("a,b,c");

    /// <summary>An unbound prefix is XPST0081 wherever it appears (K2-NameTest-35/36).</summary>
    [Theory]
    [InlineData("schema-element(notDeclared:ncname)")]
    [InlineData("schema-attribute(notDeclared:ncname)")]
    [InlineData("1 instance of schema-element(nd:x)")]
    public async Task An_unbound_prefix_in_a_schema_test_is_XPST0081(string query)
    {
        var act = async () => await _facade.EvaluateAsync(query);
        (await act.Should().ThrowAsync<System.Exception>()).Which.Message.Should().Contain("XPST0081");
    }
}
