using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// ParseSerializationOptions — the reader behind fn:serialize's parameter map and parameter element —
/// read 11 serialization parameters. SerializationOptions and the serializer support eight more
/// (doctype-system, doctype-public, version, normalization-form, suppress-indentation,
/// include-content-type, media-type, escape-uri-attributes), but nothing set them from parameters, so
/// fn:serialize silently ignored them.
/// </summary>
public class SerializationParameterCompletenessTests
{
    private static Task<string> Eval(string query) => new XQueryFacade().EvaluateAsync(query);

    private const string OutputNs = "xmlns:output='http://www.w3.org/2010/xslt-xquery-serialization'";

    // The html method writes a requested DOCTYPE; the xml method does not write one at all (a separate
    // serializer gap), so these exercise the parameters through the method that honours them.
    [Fact]
    public async Task Doctype_system_is_written()
        => (await Eval("serialize(<html><body/></html>, map { 'method': 'html', 'doctype-system': 'http://example.com/a.dtd' })"))
            .Should().Contain("SYSTEM \"http://example.com/a.dtd\"");

    [Fact]
    public async Task Doctype_public_is_written()
        => (await Eval("serialize(<html><body/></html>, map { 'method': 'html', 'doctype-public': '-//X//DTD A//EN', 'doctype-system': 'a.dtd' })"))
            .Should().Contain("PUBLIC \"-//X//DTD A//EN\"");

    [Fact]
    public async Task Version_is_written_in_the_declaration()
        => (await Eval("serialize(<a/>, map { 'method': 'xml', 'version': '1.1', 'omit-xml-declaration': false() })"))
            .Should().StartWith("<?xml version=\"1.1\"");

    [Fact]
    public async Task Normalization_form_is_applied()
        => (await Eval("serialize('e' || codepoints-to-string(769), map { 'method': 'text', 'normalization-form': 'NFC' })"))
            .Should().Be("é");

    [Fact]
    public async Task Suppress_indentation_keeps_the_element_on_one_line()
        => (await Eval("serialize(<a><b><c/><c/></b></a>, map { 'method': 'xml', 'indent': true(), 'suppress-indentation': xs:QName('b') })"))
            .Should().Contain("<b><c/><c/></b>");

    [Fact]
    public async Task Include_content_type_false_omits_the_meta_element()
        => (await Eval("serialize(<html><head><title>t</title></head></html>, map { 'method': 'html', 'include-content-type': false() })"))
            .Should().NotContain("http-equiv");

    [Fact]
    public async Task Media_type_is_used_in_the_content_type()
        => (await Eval("serialize(<html><head><title>t</title></head></html>, map { 'method': 'html', 'media-type': 'application/xhtml+xml' })"))
            .Should().Contain("application/xhtml+xml");

    // escape-uri-attributes %-escapes NON-ASCII characters only (Serialization 3.1 §7.3), so the URI
    // must contain one for the parameter to make a difference.
    [Fact]
    public async Task Escape_uri_attributes_false_leaves_a_uri_unescaped()
        => (await Eval("serialize(<html><body><a href='b&#xE9;b&#xE9;'/></body></html>, map { 'method': 'html', 'escape-uri-attributes': false() })"))
            .Should().Contain("href=\"bébé\"");

    [Fact]
    public async Task Parameter_element_doctype_system_is_written()
        => (await Eval($"serialize(<html><body/></html>, <output:serialization-parameters {OutputNs}><output:method value='html'/><output:doctype-system value='x.dtd'/></output:serialization-parameters>)"))
            .Should().Contain("SYSTEM \"x.dtd\"");

    // Guards: behaviour that was already right stays right.
    [Fact]
    public async Task Non_ascii_uri_attributes_are_still_escaped_by_default()
        => (await Eval("serialize(<html><body><a href='b&#xE9;b&#xE9;'/></body></html>, map { 'method': 'html' })"))
            .Should().Contain("href=\"b%C3%A9b%C3%A9\"");

    [Fact]
    public async Task No_doctype_without_the_parameter()
        => (await Eval("serialize(<a/>, map { 'method': 'xml' })")).Should().Be("<a/>");
}
