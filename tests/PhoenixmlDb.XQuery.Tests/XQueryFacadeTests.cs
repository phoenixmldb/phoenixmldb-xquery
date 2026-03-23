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
}
