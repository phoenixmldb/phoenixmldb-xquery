using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// serialize() wrote "&lt;e/&gt;" for XmlWriter's "&lt;e /&gt;" with a regex over the whole
/// result, so " />" lost its space everywhere: in a comment, a processing instruction, a CDATA
/// section, and in the text and adaptive methods. Only the empty tag is changed now. And the XML
/// declaration names the encoding the user gave, or "UTF-8", on every path: the document path wrote
/// XmlWriter's "utf-8", the element path the .NET name in capitals. Expected values are
/// Saxon-HE 12.10's.
/// </summary>
public sealed class EmptyTagAndDeclarationEncodingTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("serialize(<r><!-- a /> b --><?p x /> ?><e/></r>)", "<r><!-- a /> b --><?p x /> ?><e/></r>")]
    [InlineData("serialize(<r><c>a /&gt; b</c><e/></r>, map{'cdata-section-elements': xs:QName('c')})", "<r><c><![CDATA[a /> b]]></c><e/></r>")]
    [InlineData("serialize(<r><s>a<e/><!-- x /> --></s></r>, map{'indent': true(), 'suppress-indentation': xs:QName('s')})", "<r>\n  <s>a<e/><!-- x /> --></s>\n</r>")]
    [InlineData("serialize('a /> b', map{'method': 'text'})", "a /> b")]
    [InlineData("serialize('a /> b', map{'method': 'adaptive'})", "a /> b")]
    public async Task Only_the_empty_tag_loses_its_space(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Fact]
    public async Task The_xhtml_method_keeps_the_space_in_an_empty_tag()
        => (await _facade.EvaluateAsync("serialize(<html xmlns='http://www.w3.org/1999/xhtml'><body><br/></body></html>, map{'method': 'xhtml'})"))
            .Should().Contain("<br />");

    [Theory]
    [InlineData("document{<r/>}", "", """<?xml version="1.0" encoding="UTF-8"?><r/>""")]
    [InlineData("<r/>", "", """<?xml version="1.0" encoding="UTF-8"?><r/>""")]
    [InlineData("document{'t', <r/>}", "", """<?xml version="1.0" encoding="UTF-8"?>t<r/>""")]
    [InlineData("document{<r/>}", ", 'encoding': 'iso-8859-1'", """<?xml version="1.0" encoding="iso-8859-1"?><r/>""")]
    [InlineData("<r/>", ", 'encoding': 'iso-8859-1'", """<?xml version="1.0" encoding="iso-8859-1"?><r/>""")]
    [InlineData("document{<r/>}", ", 'standalone': true()", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><r/>""")]
    [InlineData("<r/>", ", 'standalone': false()", """<?xml version="1.0" encoding="UTF-8" standalone="no"?><r/>""")]
    public async Task The_declaration_names_the_encoding_as_given_or_UTF_8(string node, string more, string expected)
        => (await _facade.EvaluateAsync($"serialize({node}, map{{'omit-xml-declaration': false(){more}}})")).Should().Be(expected);

    [Fact]
    public async Task An_indented_document_that_starts_with_a_comment_starts_on_the_next_line()
        => (await _facade.EvaluateAsync("serialize(document{<!--c-->, <r><e/></r>}, map{'omit-xml-declaration': false(), 'indent': true()})"))
            .Should().Be("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!--c-->\n<r>\n  <e/>\n</r>");
}
