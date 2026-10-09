using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A host's resolver is told which module asks for a resource (#224). The request carries the
/// static base URI in force at the call, which a relative location is relative to, and
/// <see cref="ResourceRequest.ModuleUri"/>: where the calling module was loaded from, which the
/// module cannot change. Before, it carried the main module's base URI, or nothing.
/// </summary>
public sealed class ResolverLearnsTheCallingModuleTests
{
    private const string MainUri = "mem://app/main.xq";
    private const string LibraryUri = "mem://app/lib/a.xqm";

    private sealed class Recorder : ResourceResolverBase
    {
        public string Library { get; set; } = "";
        public List<(ResourceAccessKind Access, string Location, string? Base)> Requests { get; } = [];
        public List<(string Location, string? Module)> Modules { get; } = [];
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
            Modules.Add((absolute, request.ModuleUri?.AbsoluteUri));
            return null;
        }

        public override bool? IsAvailable(ResourceRequest request)
        {
            AvailabilityRequests.Add((request.Access, request.Location, request.BaseUri?.AbsoluteUri));
            Modules.Add((request.Location, request.ModuleUri?.AbsoluteUri));
            return Available;
        }
    }

    private static async Task<(Recorder Host, string Outcome)> Run(string expression, bool inLibrary, bool? available = null, string prolog = "")
    {
        var host = new Recorder { Library = $"module namespace a = 'urn:a'; {prolog} declare function a:f() {{ {expression} }};", Available = available };
        var facade = new XQueryFacade
        {
            ResourcePolicy = ResourcePolicy.CreateBuilder().WithResourceResolver(host).AllowDtdProcessing().Build(),
        };
        var query = inLibrary
            ? "import module namespace a = 'urn:a' at 'lib/a.xqm'; string((a:f())[1])"
            : $"{prolog} string(({expression})[1])";
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

        inMain.Modules.Should().OnlyContain(m => m.Module == MainUri);
        inLibrary.Modules.Should().OnlyContain(m => m.Module == LibraryUri);
    }

    /// <summary>
    /// A module chooses its static base URI. It does not choose where it was loaded from, and
    /// that is what the host is told about who asks.
    /// </summary>
    [Theory]
    [InlineData("doc('d.xml')")]
    [InlineData("doc-available('d.xml')")]
    [InlineData("unparsed-text('t.txt')")]
    [InlineData("json-doc('j.json')")]
    [InlineData("parse-xml('<!DOCTYPE r [<!ENTITY e SYSTEM \"e.ent\">]><r>&amp;e;</r>')")]
    public async Task A_module_that_declares_another_base_uri_is_still_known_by_where_it_is(string expression)
    {
        const string Declared = "declare base-uri 'mem://elsewhere/trusted/';";

        var (inLibrary, _) = await Run(expression, inLibrary: true, prolog: Declared);
        inLibrary.Modules.Should().NotBeEmpty().And.OnlyContain(m => m.Module == LibraryUri);
        inLibrary.Modules.Should().OnlyContain(m => m.Location.StartsWith("mem://elsewhere/trusted/", StringComparison.Ordinal));

        var (inMain, _) = await Run(expression, inLibrary: false, prolog: Declared);
        inMain.Modules.Should().NotBeEmpty().And.OnlyContain(m => m.Module == MainUri);
    }

    [Fact]
    public async Task A_function_item_runs_as_the_module_that_made_it()
    {
        // The library hands back a closure and a reference; the main module calls them.
        var (closure, _) = await Run("function() { doc('d.xml') }", inLibrary: true);
        closure.Modules.Should().BeEmpty();

        var host = new Recorder { Library = "module namespace a = 'urn:a'; declare function a:f() { function() { doc('d.xml') } }; declare function a:g() { doc#1 };" };
        var facade = new XQueryFacade { ResourcePolicy = ResourcePolicy.CreateBuilder().WithResourceResolver(host).Build() };
        foreach (var call in new[] { "a:f()()", "a:g()('d.xml')" })
        {
            host.Modules.Clear();
            try
            {
                await facade.EvaluateAsync("import module namespace a = 'urn:a' at 'lib/a.xqm'; " + call, inputXml: null, baseUri: null, queryBaseUri: new Uri(MainUri));
            }
            catch (PhoenixmlDb.XQuery.Execution.XQueryRuntimeException)
            {
                // nothing is supplied; what the host was asked is the subject
            }
            host.Modules.Should().NotBeEmpty(call).And.OnlyContain(m => m.Module == LibraryUri, call);
        }
    }

    [Fact]
    public async Task An_import_names_the_module_that_imports()
    {
        var host = new Recorder { Library = "module namespace a = 'urn:a'; import module namespace c = 'urn:c' at 'c.xqm'; declare function a:f() { 1 };" };
        var facade = new XQueryFacade { ResourcePolicy = ResourcePolicy.CreateBuilder().WithResourceResolver(host).Build() };
        try
        {
            await facade.EvaluateAsync("import module namespace a = 'urn:a' at 'lib/a.xqm'; a:f()", inputXml: null, baseUri: null, queryBaseUri: new Uri(MainUri));
        }
        catch (PhoenixmlDb.XQuery.Execution.XQueryRuntimeException) { }
        catch (InvalidOperationException) { }

        host.Modules.Should().Contain(("mem://app/lib/c.xqm", LibraryUri));
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
