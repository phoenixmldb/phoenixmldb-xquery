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
    // fn:max/min keep the winner's string subtype: strings are not converted (fn-max-13/-18, fn-min-13/-18).
    [InlineData("""max((xs:NCName('c'), xs:ID('b'), xs:token('a'))) instance of xs:NCName""")]
    [InlineData("""min((xs:NCName('c'), xs:ID('b'), xs:token('a'))) instance of xs:token""")]
    [InlineData("""max((xs:token("zither"), xs:anyURI("http://b.com"))) instance of xs:token""")]
    // …and compare as strings, not numbers: "9" > "10".
    [InlineData("""max((xs:token('10'), xs:token('9'))) eq '9'""")]
    // The list-type constructors return the member type, however they are called (CastAs-ListType-9..11).
    [InlineData("""xs:IDREFS("a b c") instance of xs:IDREF*""")]
    [InlineData("""let $f := xs:IDREFS#1 return $f("a b c") instance of xs:IDREF*""")]
    [InlineData("""let $f := function-lookup(QName('http://www.w3.org/2001/XMLSchema', 'IDREFS'), 1) return $f("a b c") instance of xs:IDREF*""")]
    [InlineData("""let $f := xs:IDREFS(?) return $f("a b c") instance of xs:IDREF*""")]
    [InlineData("""xs:NMTOKENS("a b") instance of xs:NMTOKEN*""")]
    [InlineData("""xs:ENTITIES("a b") instance of xs:ENTITY*""")]
    // A constructed document matches document-node(element(name)) (NodeTest004's shape).
    [InlineData("""document { <Root/> } instance of document-node(element(Root))""")]
    [InlineData("""typeswitch (document { <Root/> }) case document-node(element(Root)) return true() default return false()""")]
    [InlineData("""declare function local:f($d as document-node(element(Root))) { true() }; local:f(document { <Root/> })""")]
    [InlineData("""let $f := function($d as document-node(element(Root))) { true() } return $f(document { <Root/> })""")]
    public async Task The_result_has_the_specified_type(string query)
        => (await _facade.EvaluateAsync(query)).Should().Be("true");

    /// <summary>The document-node name test still rejects the wrong name.</summary>
    [Theory]
    [InlineData("""document { <Root/> } instance of document-node(element(Other))""")]
    [InlineData("""parse-xml("<Root/>") instance of document-node(element(Other))""")]
    public async Task A_document_with_another_element_does_not_match(string query)
        => (await _facade.EvaluateAsync(query)).Should().Be("false");

    /// <summary>A list-type constructor validates its members as <c>cast as</c> does.</summary>
    [Theory]
    [InlineData("""xs:IDREFS("1a")""")]
    [InlineData("""xs:NMTOKENS("")""")]
    public async Task An_invalid_list_value_raises_FORG0001(string query)
    {
        var act = async () => await _facade.EvaluateAsync(query);
        var ex = await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>();
        ex.Which.ErrorCode.Should().Be("FORG0001");
    }

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
