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
}
