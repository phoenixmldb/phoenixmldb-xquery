using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Character maps (use-character-maps) must replace characters only in text and
/// attribute-VALUE content, never in markup such as element or attribute NAMES.
/// Regression for the whole-string post-pass that corrupted the attribute name
/// `att` into `AAAtt` when `a` was mapped (QT3 Serialization-xml-03).
/// </summary>
public class CharacterMapSerializationTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task Character_map_does_not_corrupt_attribute_or_element_names()
    {
        // Map a→AAA, b→BBB, c→CCC. The attribute NAME "att" and element name "out"
        // must be left intact; only the attribute VALUE "abc" and the text "XabcX" map.
        const string q = """
            fn:serialize(
              <out att="abc">XabcX</out>,
              map { 'method': 'xml', 'use-character-maps': map { 'a':'AAA', 'b':'BBB', 'c':'CCC' } })
            """;
        var result = await _facade.EvaluateAsync(q, "<x/>");

        result.Should().Be("<out att=\"AAABBBCCC\">XAAABBBCCCX</out>");
    }

    [Fact]
    public async Task Character_map_replacement_is_emitted_verbatim_not_escaped()
    {
        // A replacement string is output verbatim (no XML escaping) per Serialization 4.0 §6.
        const string q = """
            fn:serialize(
              <p>S</p>,
              map { 'method': 'xml', 'use-character-maps': map { 'S':'&lt;br/&gt;' } })
            """;
        var result = await _facade.EvaluateAsync(q, "<x/>");

        result.Should().Be("<p><br/></p>");
    }
}
