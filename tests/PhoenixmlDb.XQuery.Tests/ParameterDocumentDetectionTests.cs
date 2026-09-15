using FluentAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// output:parameter-document through XQueryFacade.DetectSerializationOptions(query, staticBaseUri) — the
/// overload a caller that runs the plan itself (the QT3 runner) uses to resolve a relative document.
/// The parameter document's parameters apply unless an explicit declaration overrides them, whatever the
/// declarations' order (QT3 Serialization-xml-04); a document that cannot be used is XQST0119.
/// </summary>
public sealed class ParameterDocumentDetectionTests : IDisposable
{
    private const string Output = "declare namespace output = \"http://www.w3.org/2010/xslt-xquery-serialization\"; ";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-paramdoc-" + Guid.NewGuid().ToString("N"));

    public ParameterDocumentDetectionTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "params.xml"), """
            <output:serialization-parameters xmlns:output="http://www.w3.org/2010/xslt-xquery-serialization">
              <output:method value="xml"/>
              <output:indent value="yes"/>
              <output:omit-xml-declaration value="no"/>
              <output:cdata-section-elements value="in"/>
              <output:use-character-maps>
                <output:character-map character="a" map-string="AAA"/>
              </output:use-character-maps>
            </output:serialization-parameters>
            """);
        File.WriteAllText(Path.Combine(_dir, "html-params.xml"), """
            <output:serialization-parameters xmlns:output="http://www.w3.org/2010/xslt-xquery-serialization">
              <output:method value="html"/>
              <output:include-content-type value="no"/>
              <output:escape-uri-attributes value="no"/>
            </output:serialization-parameters>
            """);
        File.WriteAllText(Path.Combine(_dir, "not-params.xml"), "<something-else/>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private Uri BaseUri => new(_dir + Path.DirectorySeparatorChar);

    [Fact]
    public void A_parameter_document_is_applied()
    {
        var options = XQueryFacade.DetectSerializationOptions(
            Output + "declare option output:parameter-document \"params.xml\"; <out/>", BaseUri);

        options.Method.Should().Be(OutputMethod.Xml);
        options.Indent.Should().BeTrue();
        options.CdataSectionElements.Should().Contain("in");
        options.CharacterMaps.Should().ContainKey("a").WhoseValue.Should().Be("AAA");
    }

    [Fact]
    public void Explicit_declarations_override_the_parameter_document_in_any_order()
    {
        var options = XQueryFacade.DetectSerializationOptions(
            Output + "declare option output:indent \"no\"; declare option output:parameter-document \"params.xml\"; declare option output:omit-xml-declaration \"yes\"; <out/>",
            BaseUri);

        options.Indent.Should().BeFalse();
        options.OmitXmlDeclaration.Should().BeTrue();
        options.CdataSectionElements.Should().Contain("in", "what the declarations do not override still comes from the document");
    }

    // include-content-type and escape-uri-attributes are serialization parameters; a parameter document
    // that sets them must not be rejected as naming an unknown parameter (SEPM0017).
    [Fact]
    public void A_parameter_document_may_set_html_uri_and_content_type_parameters()
    {
        var options = XQueryFacade.DetectSerializationOptions(
            Output + "declare option output:parameter-document \"html-params.xml\"; <html/>", BaseUri);

        options.IncludeContentType.Should().BeFalse();
        options.EscapeUriAttributes.Should().BeFalse();
    }

    [Fact]
    public void A_missing_parameter_document_is_XQST0119()
        => FluentActions.Invoking(() => XQueryFacade.DetectSerializationOptions(
                Output + "declare option output:parameter-document \"missing.xml\"; <a/>", BaseUri))
            .Should().Throw<XQueryRuntimeException>().Where(e => e.ErrorCode == "XQST0119");

    [Fact]
    public void A_document_that_is_not_serialization_parameters_is_XQST0119()
        => FluentActions.Invoking(() => XQueryFacade.DetectSerializationOptions(
                Output + "declare option output:parameter-document \"not-params.xml\"; <a/>", BaseUri))
            .Should().Throw<XQueryRuntimeException>().Where(e => e.ErrorCode == "XQST0119");

    [Fact]
    public void A_relative_parameter_document_without_a_base_uri_is_XQST0119()
        => FluentActions.Invoking(() => XQueryFacade.DetectSerializationOptions(
                Output + "declare option output:parameter-document \"params.xml\"; <a/>", staticBaseUri: null))
            .Should().Throw<XQueryRuntimeException>().Where(e => e.ErrorCode == "XQST0119");
}
