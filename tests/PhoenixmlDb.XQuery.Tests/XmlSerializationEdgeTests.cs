using FluentAssertions;
using PhoenixmlDb.Xdm;
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

    [Fact]
    public void Cdata_section_element_splits_around_non_encodable_char()
    {
        // QT3 K2-Serialization-35: under encoding="us-ascii", a non-ASCII char (nbsp) inside a
        // cdata-section-element cannot live in the literal CDATA section — it is emitted as a
        // numeric character reference, splitting the section.
        var store = new XdmDocumentStore();
        var doc = store.LoadFromString("<chapter><para><b>bold\u00a0as brass</b></para></chapter>");
        Xdm.Nodes.XdmElement? chapter = null;
        foreach (var childId in doc.Children)
            if (store.GetNode(childId) is Xdm.Nodes.XdmElement e) { chapter = e; break; }

        var options = new SerializationOptions
        {
            Method = OutputMethod.Xml,
            Encoding = "us-ascii",
            OmitXmlDeclaration = true,
            CdataSectionElements = new HashSet<string> { "b" },
        };
        var result = XQueryResultSerializer.Serialize(chapter, store, options);

        result.Should().Contain("<![CDATA[bold]]>&#160;<![CDATA[as brass]]>");
    }

    [Fact]
    public void Top_level_array_xml_flattens_with_separator_and_declaration()
    {
        // QT3 Serialization-xml-01: a top-level array under the XML method is flattened to its
        // members joined by the item-separator, with an XML declaration (omit-xml-declaration
        // is "no" here). Driven through the static serializer to control the omit default.
        var store = new XdmDocumentStore();
        var options = new SerializationOptions
        {
            Method = OutputMethod.Xml,
            ItemSeparator = "|",
            OmitXmlDeclaration = false,
        };
        var array = new List<object?> { 1L, 2L, 3L, 4L, 5L };

        var result = XQueryResultSerializer.Serialize(array, store, options);

        result.Should().MatchRegex(@"^<\?xml[^>]+>1\|2\|3\|4\|5$");
    }
}
