using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Single-query form of the namespace-id collision, from the phoenixml DB engine: a context
/// document with 8+ namespaces is parsed into the store (ids from 101), and the compilation offers
/// ids from 108 for the constructor's namespaces.
/// </summary>
public class DbEngineNamespaceReproTests
{
    private const string NamespaceRichDoc =
        "<book xmlns:a1=\"urn:ud1\" xmlns:a2=\"urn:ud2\" xmlns:a3=\"urn:ud3\" xmlns:a4=\"urn:ud4\" " +
        "xmlns:a5=\"urn:ud5\" xmlns:a6=\"urn:ud6\" xmlns:a7=\"urn:ud7\" xmlns:a8=\"urn:ud8\" xmlns:a9=\"urn:ud9\">" +
        "<a1:x/><a2:x/><a3:x/><a4:x/><a5:x/><a6:x/><a7:x/><a8:x/><a9:x/></book>";

    [Fact]
    public async Task Constructor_namespaces_survive_a_namespace_rich_context_document()
    {
        var r = await new XQueryFacade().EvaluateAsync(
            "/book ! <uq5 xmlns=\"urn:uq5\" xmlns:p=\"urn:uqa5\" p:uqa5=\"x\"/>", NamespaceRichDoc);
        r.Should().Contain("xmlns=\"urn:uq5\"").And.Contain("xmlns:p=\"urn:uqa5\"").And.NotContain("urn:ud");
    }

    [Fact]
    public async Task Namespace_uri_of_a_constructed_element_after_a_namespace_rich_context_document()
        => (await new XQueryFacade().EvaluateAsync(
            "/book ! namespace-uri(<uq5 xmlns=\"urn:uq5\"/>)", NamespaceRichDoc)).Should().Be("urn:uq5");
}
