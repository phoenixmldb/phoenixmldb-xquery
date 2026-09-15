using FluentAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// XQueryFacade.DetectSerializationOptions — the prolog reader the facade and the QT3 runner use —
/// had its own parser for 11 options. It ignored output:parameter-document entirely (so a parameter
/// document's character maps, cdata-section-elements and the rest never applied: QT3
/// Serialization-json-34..39, -53, -54, Serialization-xml-03, -04, Serialization-035) and compared
/// yes/no options with "yes", so omit-xml-declaration " false " or "0" read as false (QT3
/// K2-Serialization-38, -39). It now builds a parameter map — the parameter document, overridden by
/// explicit declarations — and reads it with the ParseSerializationOptions that fn:serialize uses.
/// Names in cdata-section-elements and suppress-indentation are expanded with the prolog's namespace
/// declarations and default element namespace (QT3 K2-Serialization-29, -30, -31, Serialization-html-18,
/// -19a, -19b, Serialization-xhtml-18, -19a, -19b, -19c). These tests use only APIs that existed before
/// the change; ParameterDocumentDetectionTests covers the base-URI overload.
/// </summary>
public sealed class PrologSerializationOptionsTests : IDisposable
{
    private const string Output = "declare namespace output = \"http://www.w3.org/2010/xslt-xquery-serialization\"; ";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-prologopts-" + Guid.NewGuid().ToString("N"));

    public PrologSerializationOptionsTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "params.xml"), """
            <output:serialization-parameters xmlns:output="http://www.w3.org/2010/xslt-xquery-serialization">
              <output:method value="xml"/>
              <output:use-character-maps>
                <output:character-map character="a" map-string="AAA"/>
              </output:use-character-maps>
            </output:serialization-parameters>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData(" false ")]
    [InlineData("0")]
    public void Yes_no_options_accept_boolean_lexical_forms(string value)
        => XQueryFacade.DetectSerializationOptions(Output + $"declare option output:method \"xml\"; declare option output:omit-xml-declaration \"{value}\"; <a/>")
            .ForceXmlDeclaration.Should().BeTrue("an explicit omit-xml-declaration of no asks for a declaration");

    [Fact]
    public void Indent_true_reads_as_yes()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:indent \"true\"; <a/>").Indent.Should().BeTrue();

    // QT3 Serialization-xml-03 in miniature, through the facade and the query base URI it already accepted.
    [Fact]
    public async Task A_parameter_document_character_map_reaches_the_output()
        => (await new XQueryFacade().EvaluateAsync(
                Output + "declare option output:parameter-document \"params.xml\"; declare option output:omit-xml-declaration \"yes\"; <out att=\"abc\">XabcX</out>",
                queryBaseUri: new Uri(_dir + Path.DirectorySeparatorChar)))
            .Should().Be("<out att=\"AAAbc\">XAAAbcX</out>");

    [Fact]
    public async Task A_relative_parameter_document_without_a_base_uri_is_XQST0119()
        => await FluentActions.Awaiting(() => new XQueryFacade().EvaluateAsync(
                Output + "declare option output:parameter-document \"params.xml\"; <a/>"))
            .Should().ThrowAsync<XQueryRuntimeException>().Where(e => e.ErrorCode == "XQST0119");

    [Fact]
    public void A_prefixed_cdata_section_element_name_is_expanded()
        => XQueryFacade.DetectSerializationOptions(Output + "declare namespace p = \"urn:p\"; declare option output:cdata-section-elements \"b p:b\"; <a/>")
            .CdataSectionElements.Should().BeEquivalentTo(new[] { "b", "Q{urn:p}b" });

    [Fact]
    public void An_unprefixed_name_takes_the_default_element_namespace()
        => XQueryFacade.DetectSerializationOptions(Output + "declare default element namespace \"urn:d\"; declare option output:cdata-section-elements \"b\"; <a/>")
            .CdataSectionElements.Should().BeEquivalentTo(new[] { "Q{urn:d}b" });

    [Fact]
    public void Suppress_indentation_names_are_read_and_expanded()
        => XQueryFacade.DetectSerializationOptions(Output + "declare namespace p = \"urn:p\"; declare option output:suppress-indentation \" p   p:para \"; <a/>")
            .SuppressIndentation.Should().BeEquivalentTo(new[] { "p", "Q{urn:p}para" });

    [Fact]
    public void An_eqname_method_is_read()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:method \" Q{}xml\t\"; <a/>")
            .Method.Should().Be(OutputMethod.Xml);

    // QT3 K2-Serialization-29 in miniature: a prefixed suppress-indentation name must stop indentation inside it.
    [Fact]
    public async Task A_prefixed_suppress_indentation_name_keeps_its_content_on_one_line()
        => (await new XQueryFacade().EvaluateAsync(
                Output + "declare namespace p = \"urn:p\"; declare option output:method \"xml\"; declare option output:indent \"yes\"; declare option output:suppress-indentation \"p:para\"; <chapter><section><p:para><b>bold</b><i>italic</i></p:para></section></chapter>"))
            .Should().Contain("<b>bold</b><i>italic</i>");

    // QT3 K2-Serialization-30 in miniature: an element named by a prefixed cdata-section-elements name gets CDATA.
    [Fact]
    public async Task A_prefixed_cdata_section_element_gets_cdata()
        => (await new XQueryFacade().EvaluateAsync(
                Output + "declare namespace p = \"urn:p\"; declare option output:method \"xml\"; declare option output:cdata-section-elements \"p:b\"; <a><p:b>BOLD</p:b></a>"))
            .Should().Contain("<![CDATA[BOLD]]>");

    // Guards: options the old reader handled keep their meaning.
    [Fact]
    public void An_explicit_no_still_reads_as_no()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:method \"xml\"; declare option output:omit-xml-declaration \"no\"; <a/>")
            .ForceXmlDeclaration.Should().BeTrue();

    [Fact]
    public void Inline_html_version_is_still_read()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:method \"html\"; declare option output:html-version \"5.0\"; <a/>")
            .HtmlVersion.Should().Be(5.0);

    [Fact]
    public void Inline_cdata_section_elements_is_a_list()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:cdata-section-elements \"a b\"; <a/>")
            .CdataSectionElements.Should().BeEquivalentTo(new[] { "a", "b" });

    [Fact]
    public void Without_output_declarations_the_defaults_are_unchanged()
    {
        var options = XQueryFacade.DetectSerializationOptions("<a/>");
        options.Method.Should().Be(OutputMethod.Adaptive);
        options.OmitXmlDeclaration.Should().BeFalse();
        options.ForceXmlDeclaration.Should().BeFalse();
        options.Indent.Should().BeFalse();
    }
}
