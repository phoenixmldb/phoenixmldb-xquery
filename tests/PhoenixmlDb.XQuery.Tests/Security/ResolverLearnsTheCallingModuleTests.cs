using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A host's resolver is told which module asks for a resource: the request carries the static
/// base URI of the module that contains the call (#224). It carried the main module's, or
/// nothing, so a host could not tell a call in a library module from one in the query.
/// </summary>
public sealed class ResolverLearnsTheCallingModuleTests
{
    private const string MainUri = "mem://app/main.xq";
    private const string LibraryUri = "mem://app/lib/a.xqm";

    private sealed class Recorder : ResourceResolverBase
    {
        public string Library { get; set; } = "";
        public List<(ResourceAccessKind Access, string Location, string? Base)> Requests { get; } = [];
        public List<(ResourceAccessKind Access, string Location, string? Base)> AvailabilityRequests { get; } = [];
        public bool? Available { get; set; }
        public override bool SuppliesAllContent => true;

        public override ResourceContent? ResolveContent(ResourceRequest request)
        {
            var absolute = request.BaseUri != null && Uri.TryCreate(request.BaseUri, request.Location, out var joined)
                ? joined.AbsoluteUri
                : request.Location;
            if (absolute == LibraryUri)
                return new ResourceContent(Library, new Uri(absolute));
            if (absolute.EndsWith("b.xqm", StringComparison.Ordinal))
                return new ResourceContent("module namespace b = 'urn:b'; declare function b:g() { 41 };", new Uri(absolute));
            Requests.Add((request.Access, absolute, request.BaseUri?.AbsoluteUri));
            return null;
        }

        public override bool? IsAvailable(ResourceRequest request)
        {
            AvailabilityRequests.Add((request.Access, request.Location, request.BaseUri?.AbsoluteUri));
            return Available;
        }
    }

    private static async Task<(Recorder Host, string Outcome)> Run(string expression, bool inLibrary, bool? available = null)
    {
        var host = new Recorder { Library = $"module namespace a = 'urn:a'; declare function a:f() {{ {expression} }};", Available = available };
        var facade = new XQueryFacade
        {
            ResourcePolicy = ResourcePolicy.CreateBuilder().WithResourceResolver(host).AllowDtdProcessing().Build(),
        };
        var query = inLibrary
            ? "import module namespace a = 'urn:a' at 'lib/a.xqm'; string((a:f())[1])"
            : $"string(({expression})[1])";
        try
        {
            return (host, (await facade.EvaluateAsync(query, inputXml: null, baseUri: null, queryBaseUri: new Uri(MainUri))).Trim());
        }
#pragma warning disable CA1031 // the resolver supplies nothing: the load fails, and what it was asked is the subject
        catch (Exception e)
#pragma warning restore CA1031
        {
            return (host, "ERR " + e.Message);
        }
    }

    [Theory]
    [InlineData("doc('d.xml')", ResourceAccessKind.ReadDocument, "d.xml")]
    [InlineData("unparsed-text('t.txt')", ResourceAccessKind.ReadText, "t.txt")]
    [InlineData("unparsed-text-lines('t.txt')", ResourceAccessKind.ReadText, "t.txt")]
    [InlineData("unparsed-text-available('t.txt')", ResourceAccessKind.ReadText, "t.txt")]
    [InlineData("json-doc('j.json')", ResourceAccessKind.ReadText, "j.json")]
    [InlineData("parse-xml('<!DOCTYPE r [<!ENTITY e SYSTEM \"e.ent\">]><r>&amp;e;</r>')", ResourceAccessKind.ReadDocument, "e.ent")]
    public async Task A_load_names_the_module_that_makes_it(string expression, ResourceAccessKind access, string file)
    {
        var (inMain, _) = await Run(expression, inLibrary: false);
        inMain.Requests.Should().Contain((access, "mem://app/" + file, MainUri));

        var (inLibrary, _) = await Run(expression, inLibrary: true);
        inLibrary.Requests.Should().Contain((access, "mem://app/lib/" + file, LibraryUri));
        inLibrary.Requests.Should().NotContain(r => r.Base == MainUri || r.Base == null);
    }

    [Fact]
    public async Task A_dynamically_loaded_module_is_asked_for_by_the_module_that_loads_it()
    {
        const string Load = "load-xquery-module('urn:b', map { 'location-hints': 'b.xqm' })?functions(QName('urn:b', 'g'))?0()";

        (await Run(Load, inLibrary: true)).Outcome.Should().Be("41");
    }

    [Theory]
    [InlineData(false, MainUri, "mem://app/d.xml")]
    [InlineData(true, LibraryUri, "mem://app/lib/d.xml")]
    public async Task Doc_available_puts_the_whole_request_to_the_host(bool inLibrary, string module, string location)
    {
        var (host, outcome) = await Run("doc-available('d.xml')", inLibrary, available: true);

        outcome.Should().Be("true");
        host.AvailabilityRequests.Should().Equal((ResourceAccessKind.ReadDocument, location, module));
    }

    [Fact]
    public async Task A_host_that_does_not_answer_the_request_is_asked_by_location_as_before()
    {
        var (host, outcome) = await Run("doc-available('d.xml')", inLibrary: false, available: null);

        outcome.Should().Be("false");
        host.AvailabilityRequests.Should().HaveCount(1);
    }
}
