using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Edge cases of the XML output method: a document node whose top-level content is text (no
/// document element), and free-standing strings carrying control characters.
/// </summary>
public class XmlSerializationEdgeTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task Document_node_with_text_only_content_does_not_crash()
    {
        // QT3 K2-Serialization-15: document{1 to 5} has a single text-node child "1 2 3 4 5"
        // (no document element). The XML output method must serialize the children, not throw
        // "Token Text in state Document" from .NET's Document-conformance XmlWriter.
        var result = await _facade.EvaluateAsync(
            "fn:serialize(document { 1 to 5 }, map{'method':'xml','item-separator':'|'})", "<x/>");
        result.Should().Contain("1 2 3 4 5");
    }

    [Fact]
    public async Task Free_standing_string_escapes_carriage_return_as_ncr()
    {
        // QT3 K2-Serialization-11: a CR in character data must serialize as &#xD; so it
        // survives a reparse (XML parsers normalise raw CR / CR-LF to LF). LF stays literal.
        var result = await _facade.EvaluateAsync(
            "fn:serialize('a&#xD;b&#xD;&#xA;c', map{'method':'xml'})", "<x/>");
        result.Should().Be("a&#xD;b&#xD;\nc");
    }

    [Fact]
    public async Task Free_standing_string_escapes_markup_characters()
    {
        var result = await _facade.EvaluateAsync(
            "fn:serialize('a < b &amp; c > d', map{'method':'xml'})", "<x/>");
        result.Should().Be("a &lt; b &amp; c &gt; d");
    }
}
