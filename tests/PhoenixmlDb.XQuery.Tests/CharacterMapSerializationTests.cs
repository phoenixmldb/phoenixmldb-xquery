using AwesomeAssertions;
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

    // The keys of a character map: one character, a supplementary one included. From a map, a key
    // that is not a string is XPTY0004 and a longer one SEPM0016; from an
    // output:serialization-parameters element a longer one makes the element invalid, SEPM0017.
    // A two-character key was SEPM0016 from the element, a QName key SEPM0016 from the map, and a
    // supplementary character was refused as two (QT3 serialize-xml-023, -023b, -139, -139b).
    [Theory]
    [InlineData("map { 'use-character-maps': map { QName('http://example.org', 'xyz'): 'abc' } }", "XPTY0004")]
    [InlineData("map { 'use-character-maps': map { 'xy': 'abc' } }", "SEPM0016")]
    [InlineData("<output:serialization-parameters xmlns:output='http://www.w3.org/2010/xslt-xquery-serialization'><output:use-character-maps><output:character-map character='$$' map-string='x'/></output:use-character-maps></output:serialization-parameters>", "SEPM0017")]
    public async Task A_character_map_key_that_is_not_one_character_is_refused(string parameters, string code)
    {
        var act = () => _facade.EvaluateAsync($"serialize(<e>xml</e>, {parameters})", "<x/>");
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>()).Which.ErrorCode.Should().Be(code);
    }

    [Fact]
    public async Task A_supplementary_character_is_one_key()
        => (await _facade.EvaluateAsync("serialize(<e>a\U0001F600b</e>, map { 'use-character-maps': map { '\U0001F600': '-' } })", "<x/>"))
            .Should().Be("<e>a-b</e>");
}
