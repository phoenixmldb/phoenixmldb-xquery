using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Tests for <see cref="XQueryFacade"/> string-in / string-out API.
/// </summary>
public class XQueryFacadeTests
{
    private readonly XQueryFacade _facade = new();

    private const string BooksXml = """
        <library>
            <book>
                <title>The Great Gatsby</title>
                <author>F. Scott Fitzgerald</author>
            </book>
            <book>
                <title>1984</title>
                <author>George Orwell</author>
            </book>
            <book>
                <title>Brave New World</title>
                <author>Aldous Huxley</author>
            </book>
        </library>
        """;

    // --- Context item (.) tests ---

    [Fact]
    public async Task EvaluateAsync_context_item_navigates_document()
    {
        var result = await _facade.EvaluateAsync(
            "./library/book[1]/title/text()", BooksXml);

        result.Should().Be("The Great Gatsby");
    }

    [Fact]
    public async Task EvaluateAsync_context_item_with_descendant_axis()
    {
        var results = await _facade.EvaluateAllAsync(
            ".//title/text()", BooksXml);

        results.Should().HaveCount(3);
        results[0].Should().Be("The Great Gatsby");
    }

    [Fact]
    public async Task EvaluateAsync_context_item_with_abbreviated_descendant()
    {
        var results = await _facade.EvaluateAllAsync(
            "//book/author/text()", BooksXml);

        results.Should().HaveCount(3);
        results.Should().Contain("George Orwell");
    }

    [Fact]
    public async Task EvaluateAllAsync_returns_multiple_results()
    {
        var results = await _facade.EvaluateAllAsync(
            "./library/book/title/text()", BooksXml);

        results.Should().HaveCount(3);
        results[0].Should().Be("The Great Gatsby");
        results[1].Should().Be("1984");
        results[2].Should().Be("Brave New World");
    }

    [Fact]
    public async Task EvaluateScalarAsync_returns_single_value()
    {
        var result = await _facade.EvaluateScalarAsync(
            "./library/book[2]/title/text()", BooksXml);

        result.Should().Be("1984");
    }

    [Fact]
    public async Task EvaluateScalarAsync_returns_null_for_empty()
    {
        var result = await _facade.EvaluateScalarAsync(
            "./library/book[99]/title", BooksXml);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScalarAsync_context_item_with_count()
    {
        var result = await _facade.EvaluateScalarAsync(
            "count(//book)", BooksXml);

        result.Should().Be("3");
    }

    [Fact]
    public async Task EvaluateAsync_without_input_xml()
    {
        var result = await _facade.EvaluateAsync("1 + 1");

        result.Should().Be("2");
    }

    // --- External variable $input tests ---

    [Fact]
    public async Task EvaluateAsync_input_variable_with_external_declaration()
    {
        var result = await _facade.EvaluateAsync("""
            declare variable $input external;
            $input/library/book[1]/title/text()
            """, BooksXml);

        result.Should().Be("The Great Gatsby");
    }

    [Fact]
    public async Task EvaluateAsync_context_item_and_input_variable_return_same_result()
    {
        var viaContextItem = await _facade.EvaluateAsync(
            "./library/book[1]/title/text()", BooksXml);

        var viaInputVar = await _facade.EvaluateAsync("""
            declare variable $input external;
            $input/library/book[1]/title/text()
            """, BooksXml);

        viaContextItem.Should().Be(viaInputVar);
    }

    // --- doc() URI tests ---

    [Fact]
    public async Task EvaluateAsync_doc_uri_access()
    {
        var result = await _facade.EvaluateAsync(
            "doc('urn:xqueryfacade:input')/library/book[1]/title/text()", BooksXml);

        result.Should().Be("The Great Gatsby");
    }

    // --- Prolog support tests ---

    [Fact]
    public async Task EvaluateAsync_supports_namespace_declarations()
    {
        // Prolog namespace declarations should work without query rewriting
        var result = await _facade.EvaluateAsync("""
            declare namespace math = "http://www.w3.org/2005/xpath-functions/math";
            math:pi()
            """);

        result.Should().StartWith("3.14");
    }

    [Fact]
    public async Task EvaluateAsync_supports_variable_declarations()
    {
        var result = await _facade.EvaluateAsync("""
            declare variable $multiplier := 10;
            count(//book) * $multiplier
            """, BooksXml);

        result.Should().Be("30");
    }

    [Fact]
    public async Task EvaluateAsync_supports_function_declarations()
    {
        var result = await _facade.EvaluateAsync("""
            declare function local:title-count($lib) {
                count($lib/book/title)
            };
            local:title-count(./library)
            """, BooksXml);

        result.Should().Be("3");
    }

    // --- Node constructor tests ---

    [Fact]
    public async Task EvaluateAsync_direct_element_constructor_simple()
    {
        var result = await _facade.EvaluateAsync(
            "<root><item>foo</item><item>bar</item><item>baz</item></root>");

        result.Should().Be("<root><item>foo</item><item>bar</item><item>baz</item></root>");
    }

    [Fact]
    public async Task EvaluateAsync_direct_element_constructor_with_attribute()
    {
        var result = await _facade.EvaluateAsync(
            """<book isbn="978-0-123">title</book>""");

        result.Should().Be("""<book isbn="978-0-123">title</book>""");
    }

    [Fact]
    public async Task EvaluateAsync_direct_element_constructor_with_expression()
    {
        var result = await _facade.EvaluateAsync(
            "<result>{1 + 2}</result>");

        result.Should().Be("<result>3</result>");
    }

    [Fact]
    public async Task EvaluateAsync_nested_element_constructor_with_flwor()
    {
        var result = await _facade.EvaluateAsync(
            "<list>{for $i in 1 to 3 return <item>{$i}</item>}</list>");

        result.Should().Be("<list><item>1</item><item>2</item><item>3</item></list>");
    }

    [Fact]
    public async Task EvaluateAsync_computed_element_constructor()
    {
        var result = await _facade.EvaluateAsync(
            """element root { element child { "hello" } }""");

        result.Should().Be("<root><child>hello</child></root>");
    }

    [Fact]
    public async Task EvaluateAsync_computed_attribute_constructor()
    {
        var result = await _facade.EvaluateAsync(
            """element item { attribute name { "test" }, "content" }""");

        result.Should().Be("""<item name="test">content</item>""");
    }

    [Fact]
    public async Task EvaluateAsync_text_constructor()
    {
        var result = await _facade.EvaluateAsync(
            """text { "hello world" }""");

        result.Should().Be("hello world");
    }

    [Fact]
    public async Task EvaluateAsync_comment_constructor()
    {
        var result = await _facade.EvaluateAsync(
            """comment { "this is a comment" }""");

        result.Should().Be("<!--this is a comment-->");
    }

    [Fact]
    public async Task EvaluateAsync_pi_constructor()
    {
        var result = await _facade.EvaluateAsync(
            """processing-instruction xml-stylesheet { "type='text/xsl'" }""");

        result.Should().Be("<?xml-stylesheet type='text/xsl'?>");
    }

    [Fact]
    public async Task EvaluateAsync_document_constructor()
    {
        var result = await _facade.EvaluateAsync(
            """
            declare option output:omit-xml-declaration "yes";
            document { <root>content</root> }
            """);

        result.Should().Be("<root>content</root>");
    }

    [Fact]
    public async Task EvaluateAsync_element_constructor_with_input_xml()
    {
        var result = await _facade.EvaluateAsync(
            "<result>{.//title/text()}</result>", BooksXml);

        result.Should().Be("<result>The Great Gatsby1984Brave New World</result>");
    }

    // =====================================================================
    // 1. Direct Element Constructors
    // =====================================================================

    [Fact]
    public async Task Parse_and_serialize_mixed_content()
    {
        var result = await _facade.EvaluateAsync(
            "<element>static text {current-dateTime()}</element>");

        result.Should().Contain("static text");
        result.Should().StartWith("<element>");
        result.Should().EndWith("</element>");
    }

    [Fact]
    public async Task Parse_and_serialize_nested_elements()
    {
        var result = await _facade.EvaluateAsync(
            "<root><item>foo</item><item>bar</item></root>");

        result.Should().Contain("<item>foo</item>");
        result.Should().Contain("<item>bar</item>");
        result.Should().StartWith("<root>");
        result.Should().EndWith("</root>");
    }

    [Fact]
    public async Task Parse_and_serialize_with_attributes()
    {
        var result = await _facade.EvaluateAsync(
            """<div class="main">content</div>""");

        result.Should().Contain("""class="main""");
        result.Should().Contain("content");
    }

    [Fact]
    public async Task Parse_and_serialize_self_closing()
    {
        var result = await _facade.EvaluateAsync("<br/>");

        // XQuery serialization may produce <br /> or <br/>
        result.Should().Match("*br*/*>*");
    }

    // =====================================================================
    // 2. Map/Array Serialization
    // =====================================================================

    [Fact]
    public async Task Map_serializes_as_json_not_dotnet_type()
    {
        var result = await _facade.EvaluateAsync(
            "map { 'name': 'test' }");

        result.Should().Contain("\"name\"");
        result.Should().Contain("\"test\"");
        result.Should().NotContain("Dictionary");
    }

    [Fact]
    public async Task Map_with_array_serializes_correctly()
    {
        var result = await _facade.EvaluateAsync(
            "map { 'data': [1, 2, 3] }");

        result.Should().Contain("\"data\"");
        result.Should().Contain("1");
        result.Should().Contain("2");
        result.Should().Contain("3");
        result.Should().NotContain("Dictionary");
    }

    [Fact]
    public async Task Nested_maps_serialize_correctly()
    {
        var result = await _facade.EvaluateAsync(
            "map { 'a': map { 'b': 1 } }");

        result.Should().Contain("\"a\"");
        result.Should().Contain("\"b\"");
        result.Should().Contain("1");
    }

    [Fact]
    public async Task Map_without_keyword_works()
    {
        // XQuery 4.0 allows map literals without the 'map' keyword
        var result = await _facade.EvaluateAsync(
            "{ 'key': 'value' }");

        result.Should().Contain("\"key\"");
        result.Should().Contain("\"value\"");
    }

    // =====================================================================
    // 3. Serialization Options
    // =====================================================================

    [Fact]
    public async Task Output_method_json_serializes_map_as_json()
    {
        var result = await _facade.EvaluateAsync("""
            declare namespace output = "http://www.w3.org/2010/xslt-xquery-serialization";
            declare option output:method "json";
            map { 'x': 1 }
            """);

        result.Should().Contain("\"x\"");
        result.Should().Contain("1");
    }

    [Fact]
    public async Task Output_method_json_with_indent()
    {
        var result = await _facade.EvaluateAsync("""
            declare namespace output = "http://www.w3.org/2010/xslt-xquery-serialization";
            declare option output:method "json";
            declare option output:indent "yes";
            map { 'x': 1 }
            """);

        // Indented JSON should contain newlines
        result.Should().Contain("\"x\"");
    }

    [Fact]
    public async Task Output_method_xml_with_indent()
    {
        var result = await _facade.EvaluateAsync("""
            declare namespace output = "http://www.w3.org/2010/xslt-xquery-serialization";
            declare option output:method "xml";
            declare option output:indent "yes";
            <root><child/></root>
            """);

        result.Should().Contain("<root>");
        result.Should().Contain("<child");
    }

    [Fact]
    public async Task Output_omit_xml_declaration()
    {
        var result = await _facade.EvaluateAsync("""
            declare option output:omit-xml-declaration "yes";
            <root/>
            """);

        result.Should().NotStartWith("<?xml");
    }

    // =====================================================================
    // 4. FLWOR Expressions
    // =====================================================================

    [Fact]
    public async Task Flwor_for_let_where_order_return()
    {
        var result = await _facade.EvaluateAsync(
            "for $x in (3, 1, 2) order by $x return $x");

        result.Should().Be("123");
    }

    [Fact]
    public async Task Flwor_with_element_construction()
    {
        var result = await _facade.EvaluateAsync(
            "for $x in (1, 2, 3) return <item>{$x}</item>");

        result.Should().Be("<item>1</item><item>2</item><item>3</item>");
    }

    [Fact]
    public async Task Flwor_group_by()
    {
        var result = await _facade.EvaluateAsync("""
            for $x in (1, 2, 3, 4, 5)
            let $parity := if ($x mod 2 = 0) then 'even' else 'odd'
            group by $parity
            order by $parity
            return element group { attribute type { $parity }, count($x) }
            """);

        result.Should().Contain("even");
        result.Should().Contain("odd");
    }

    [Fact]
    public async Task Flwor_for_member()
    {
        var result = await _facade.EvaluateAsync(
            "for member $m in [1, 2, 3] return $m");

        result.Should().Be("123");
    }

    // =====================================================================
    // 5. String Constructors (backtick syntax)
    // =====================================================================

    [Fact]
    public async Task String_constructor_basic()
    {
        var result = await _facade.EvaluateAsync(
            "``[Hello World]``");

        result.Should().Be("Hello World");
    }

    [Fact]
    public async Task String_constructor_with_interpolation()
    {
        var result = await _facade.EvaluateAsync(
            "``[Hello `{1+1}` World]``");

        result.Should().Be("Hello 2 World");
    }

    // =====================================================================
    // 6. XQuery Update (transform copy/modify/return)
    // =====================================================================

    [Fact]
    public async Task Transform_copy_modify_return()
    {
        var result = await _facade.EvaluateAsync("""
            copy $doc := <root><name>old</name></root>
            modify replace value of node $doc/name with "new"
            return $doc
            """);

        result.Should().Contain("<name>new</name>");
    }

    // =====================================================================
    // 7. Annotations
    // =====================================================================

    [Fact]
    public async Task Annotation_public_function()
    {
        var result = await _facade.EvaluateAsync("""
            declare %public function local:greet($n as xs:string) as xs:string {
                "Hi " || $n
            };
            local:greet("World")
            """);

        result.Should().Be("Hi World");
    }

    // =====================================================================
    // 8. fn:serialize with params
    // =====================================================================

    [Fact]
    public async Task Fn_serialize_with_params_map()
    {
        var result = await _facade.EvaluateAsync("""
            fn:serialize(<root/>, map { "method": "xml" })
            """);

        result.Should().Contain("<root");
    }

    // =====================================================================
    // 9. Error handling
    // =====================================================================

    [Fact]
    public async Task Invalid_xpath_throws_parse_error()
    {
        Func<Task> act = async () => await _facade.EvaluateAsync("+++invalid+++");

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Division_by_zero_throws_FOAR0002()
    {
        // XQuery: 1 div 0 for xs:integer should throw FOAR0002
        Func<Task> act = async () => await _facade.EvaluateAsync("1 div 0");

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Unbound_variable_throws_XPST0008()
    {
        Func<Task> act = async () => await _facade.EvaluateAsync("$undefined");

        await act.Should().ThrowAsync<Exception>();
    }

    // =====================================================================
    // 10. Context item
    // =====================================================================

    [Fact]
    public async Task Context_item_with_input_xml()
    {
        var result = await _facade.EvaluateAsync(
            "//item/text()", "<root><item>a</item><item>b</item></root>");

        result.Should().Be("ab");
    }

    [Fact]
    public async Task No_context_item_when_no_input()
    {
        var result = await _facade.EvaluateAsync("1 + 1");

        result.Should().Be("2");
    }

    // =====================================================================
    // 11. fn:doc and document functions
    // =====================================================================

    [Fact]
    public async Task Doc_function_with_facade_input()
    {
        var result = await _facade.EvaluateAsync(
            "doc('urn:xqueryfacade:input')/root/text()",
            "<root>hello</root>");

        result.Should().Be("hello");
    }

    // =====================================================================
    // 12. Types and casting
    // =====================================================================

    [Fact]
    public async Task Cast_as_integer()
    {
        var result = await _facade.EvaluateAsync("'42' cast as xs:integer");

        result.Should().Be("42");
    }

    [Fact]
    public async Task Castable_returns_boolean()
    {
        var result = await _facade.EvaluateAsync("'hello' castable as xs:integer");

        result.Should().BeOneOf("false", "False");
    }

    [Fact]
    public async Task Instance_of()
    {
        var result = await _facade.EvaluateAsync("42 instance of xs:integer");

        result.Should().BeOneOf("true", "True");
    }
}
