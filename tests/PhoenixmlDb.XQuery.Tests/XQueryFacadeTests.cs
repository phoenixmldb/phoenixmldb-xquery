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

    [Fact]
    public async Task EvaluateAsync_returns_string_result()
    {
        var result = await _facade.EvaluateAsync(
            "$input/library/book[1]/title/text()", BooksXml);

        result.Should().Be("The Great Gatsby");
    }

    [Fact]
    public async Task EvaluateAsync_returns_xml_element()
    {
        var result = await _facade.EvaluateAsync(
            "$input/library/book[1]/title", BooksXml);

        result.Should().Contain("<title>");
        result.Should().Contain("The Great Gatsby");
        result.Should().Contain("</title>");
    }

    [Fact]
    public async Task EvaluateAllAsync_returns_multiple_results()
    {
        var results = await _facade.EvaluateAllAsync(
            "$input/library/book/title/text()", BooksXml);

        results.Should().HaveCount(3);
        results[0].Should().Be("The Great Gatsby");
        results[1].Should().Be("1984");
        results[2].Should().Be("Brave New World");
    }

    [Fact]
    public async Task EvaluateScalarAsync_returns_single_value()
    {
        var result = await _facade.EvaluateScalarAsync(
            "$input/library/book[2]/title/text()", BooksXml);

        result.Should().Be("1984");
    }

    [Fact]
    public async Task EvaluateScalarAsync_returns_null_for_empty()
    {
        var result = await _facade.EvaluateScalarAsync(
            "$input/library/book[99]/title", BooksXml);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_without_input_xml()
    {
        var result = await _facade.EvaluateAsync("1 + 1");

        result.Should().Be("2");
    }
}
