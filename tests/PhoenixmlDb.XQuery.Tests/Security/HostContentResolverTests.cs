using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A host supplies the CONTENT of what the engine loads (IResourceResolver.ResolveContent), so
/// the engine opens nothing itself. Before, for modules, schema documents, JSON resources and
/// external entities, the engine authorised a location against the policy and then opened it by
/// name: two steps, with room between them to replace the file. Everything here is served from
/// memory; none of the locations exists on disk.
/// </summary>
public sealed class HostContentResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-host-" + Guid.NewGuid().ToString("N"));

    public HostContentResolverTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class MemoryResolver(bool suppliesAll) : ResourceResolverBase
    {
        public Dictionary<string, string> Content { get; } = new(StringComparer.Ordinal);
        public List<string> Asked { get; } = [];
        public override bool SuppliesAllContent => suppliesAll;

        public override ResourceContent? ResolveContent(ResourceRequest request)
        {
            var absolute = request.BaseUri != null && Uri.TryCreate(request.BaseUri, request.Location, out var joined)
                ? joined.AbsoluteUri
                : request.Location;
            Asked.Add($"{request.Access}:{absolute}");
            return Content.TryGetValue(absolute, out var text) ? new ResourceContent(text, new Uri(absolute)) : null;
        }
    }

    private static readonly Uri QueryBase = new("mem://app/main.xq");

    private static async Task<string> Run(MemoryResolver resolver, string query, bool allowDtd = false)
    {
        var policy = ResourcePolicy.CreateBuilder().WithResourceResolver(resolver).AllowDtdProcessing(allowDtd).Build();
        var facade = new XQueryFacade { ResourcePolicy = policy };
        try
        {
            return (await facade.EvaluateAsync(query, inputXml: null, baseUri: null, queryBaseUri: QueryBase)).Trim();
        }
#pragma warning disable CA1031 // any failure is the outcome under test
        catch (Exception e)
#pragma warning restore CA1031
        {
            return "ERR " + e.Message;
        }
    }

    [Fact]
    public async Task A_module_and_the_module_it_imports_come_from_the_host()
    {
        var host = new MemoryResolver(suppliesAll: true);
        host.Content["mem://app/lib/a.xqm"] =
            "module namespace a = 'urn:a'; import module namespace b = 'urn:b' at 'b.xqm'; declare function a:f() { b:g() + 1 };";
        host.Content["mem://app/lib/b.xqm"] = "module namespace b = 'urn:b'; declare function b:g() { 41 };";

        (await Run(host, "import module namespace a = 'urn:a' at 'lib/a.xqm'; a:f()")).Should().Be("42");
        // b.xqm was relative to a.xqm's own base URI, and came back to the host.
        host.Asked.Should().Contain("ImportStylesheet:mem://app/lib/a.xqm").And.Contain("ImportStylesheet:mem://app/lib/b.xqm");
    }

    [Fact]
    public async Task A_dynamically_loaded_module_comes_from_the_host()
    {
        var host = new MemoryResolver(suppliesAll: true);
        host.Content["mem://app/lib/b.xqm"] = "module namespace b = 'urn:b'; declare function b:g() { 41 };";

        (await Run(host, "load-xquery-module('urn:b', map { 'location-hints': 'lib/b.xqm' })?functions(QName('urn:b', 'g'))?0()"))
            .Should().Be("41");
    }

    [Fact]
    public async Task A_schema_and_the_schema_it_includes_come_from_the_host()
    {
        var host = new MemoryResolver(suppliesAll: true);
        host.Content["mem://app/s/main.xsd"] = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:s" elementFormDefault="qualified">
              <xs:include schemaLocation="part.xsd"/>
            </xs:schema>
            """;
        host.Content["mem://app/s/part.xsd"] = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:s" elementFormDefault="qualified">
              <xs:element name="n" type="xs:integer"/>
            </xs:schema>
            """;

        (await Run(host, "import schema namespace s = 'urn:s' at 's/main.xsd'; data(validate { <s:n>7</s:n> }) instance of xs:integer"))
            .Should().Be("true");
        host.Asked.Should().Contain(a => a.EndsWith("mem://app/s/part.xsd", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Json_and_text_come_from_the_host()
    {
        var host = new MemoryResolver(suppliesAll: true);
        host.Content["mem://app/data/x.json"] = "{ \"a\": [1, 2, 3] }";
        host.Content["mem://app/data/t.txt"] = "héllo";

        (await Run(host, "xs:integer(sum(json-doc('data/x.json')?a?*))")).Should().Be("6");
        (await Run(host, "unparsed-text('data/t.txt')")).Should().Be("héllo");
        (await Run(host, "string-join((unparsed-text-available('data/t.txt'), unparsed-text-available('data/none.txt')) ! string(), ' ')")).Should().Be("true false");
    }

    [Fact]
    public async Task An_external_entity_comes_from_the_host()
    {
        var host = new MemoryResolver(suppliesAll: true);
        host.Content["mem://app/e.txt"] = "from the host";

        (await Run(host,
                // &amp;e; in the XQuery string literal is the text &e; in the document it parses.
                "string(parse-xml('<!DOCTYPE a [<!ENTITY e SYSTEM \"mem://app/e.txt\">]><a>&amp;e;</a>'))",
                allowDtd: true))
            .Should().Be("from the host");
    }

    [Theory]
    [InlineData("json-doc('{file}')")]
    [InlineData("unparsed-text('{file}')")]
    [InlineData("doc('{file}')")]
    [InlineData("import module namespace m = 'urn:m' at '{file}'; 1")]
    [InlineData("import schema namespace s = 'urn:s' at '{file}'; 1")]
    public async Task When_the_host_is_the_only_source_nothing_else_is_opened(string query)
    {
        // The file exists and holds something each of these could load. The host does not
        // supply it, so the load fails; the engine does not fall back to opening it.
        var file = Path.Combine(_dir, "real");
        await File.WriteAllTextAsync(file, "{ \"x\": 1 }");
        var host = new MemoryResolver(suppliesAll: true);

        var result = await Run(host, query.Replace("{file}", new Uri(file).AbsoluteUri, StringComparison.Ordinal));
        result.Should().StartWith("ERR");
    }

    [Fact]
    public async Task A_resolver_that_is_not_the_only_source_still_falls_through()
    {
        // The default: what the resolver does not supply is loaded as before, under the policy.
        var file = Path.Combine(_dir, "real.json");
        await File.WriteAllTextAsync(file, "{ \"x\": 5 }");
        var host = new MemoryResolver(suppliesAll: false);
        var policy = ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: _dir).WithResourceResolver(host).Build();
        var facade = new XQueryFacade { ResourcePolicy = policy };

        (await facade.EvaluateAsync($"xs:integer(json-doc('{new Uri(file).AbsoluteUri}')?x)", inputXml: null, baseUri: null, queryBaseUri: QueryBase))
            .Trim().Should().Be("5");
    }

    [Fact]
    public async Task A_document_supplied_as_content_is_navigable()
    {
        // ResolveDocument, the older member, must return nodes, which a host cannot build into
        // the query's own store. Content is built there by the engine.
        var host = new MemoryResolver(suppliesAll: true);
        host.Content["mem://app/d.xml"] = "<d><c>1</c><c>2</c><g><c>3</c></g></d>";
        (await Run(host, "string-join((count(doc('mem://app/d.xml')//c), sum(doc('mem://app/d.xml')//c), "
                + "doc('mem://app/d.xml') is doc('mem://app/d.xml')) ! string(), ' ')"))
            .Should().Be("3 6 true");
    }
}
