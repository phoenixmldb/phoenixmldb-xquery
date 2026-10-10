using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A host's resolver is told which module imports a schema: the request for the schema
/// document, and for each document that schema includes, carries the importing module as
/// <see cref="ResourceRequest.ModuleUri"/>.
/// </summary>
public sealed class ResolverLearnsTheSchemaImporterTests
{
    private const string MainUri = "mem://app/main.xq";
    private const string LibraryUri = "mem://app/lib/a.xqm";

    private sealed class Host : ResourceResolverBase
    {
        public Dictionary<string, string> Content { get; } = new(StringComparer.Ordinal);
        public List<(string Location, string? Module)> Asked { get; } = [];
        public override bool SuppliesAllContent => true;

        public override ResourceContent? ResolveContent(ResourceRequest request)
        {
            var absolute = request.BaseUri != null && Uri.TryCreate(request.BaseUri, request.Location, out var joined)
                ? joined.AbsoluteUri
                : request.Location;
            Asked.Add((absolute, request.ModuleUri?.AbsoluteUri));
            return Content.TryGetValue(absolute, out var text) ? new ResourceContent(text, new Uri(absolute)) : null;
        }
    }

    private const string Schema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:s" xmlns="urn:s">
          <xs:include schemaLocation="more.xsd"/>
          <xs:simpleType name="word"><xs:restriction base="xs:string"/></xs:simpleType>
        </xs:schema>
        """;

    private const string More = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:s">
          <xs:simpleType name="other"><xs:restriction base="xs:string"/></xs:simpleType>
        </xs:schema>
        """;

    private static async Task<(Host Host, string Outcome)> Run(string query, string? library = null)
    {
        var host = new Host();
        host.Content["mem://app/schemas/s.xsd"] = Schema;
        host.Content["mem://app/schemas/more.xsd"] = More;
        if (library != null)
            host.Content[LibraryUri] = library;
        var store = new XdmDocumentStore();
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: new XsdSchemaProvider())
        {
            ResourcePolicy = ResourcePolicy.CreateBuilder().WithResourceResolver(host).Build(),
        };
        try
        {
            var compiled = engine.Compile(query, new PhoenixmlDb.XQuery.Execution.CompilationOptions { BaseUri = MainUri });
            if (!compiled.Success)
                return (host, "COMPILE " + string.Join("; ", compiled.Errors.Select(e => e.Code + " " + e.Message)));
            var items = new List<object?>();
            await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                items.Add(item);
            return (host, string.Join(",", items.Select(item => item?.ToString())));
        }
#pragma warning disable CA1031 // what the host was asked is the subject, whatever the outcome
        catch (Exception e)
#pragma warning restore CA1031
        {
            return (host, "ERR " + e.Message);
        }
    }

    [Fact]
    public async Task The_main_module_imports()
    {
        var (host, outcome) = await Run("import schema namespace s = 'urn:s' at 'schemas/s.xsd'; 'a' castable as s:word");

        outcome.Should().Be("True");
        host.Asked.Where(a => a.Location.EndsWith(".xsd", StringComparison.Ordinal)).Should().HaveCountGreaterThanOrEqualTo(2)
            .And.OnlyContain(a => a.Module == MainUri);
    }

    [Fact]
    public async Task A_library_module_imports()
    {
        var (host, outcome) = await Run(
            "import module namespace a = 'urn:a' at 'lib/a.xqm'; a:f()",
            "module namespace a = 'urn:a'; import schema namespace s = 'urn:s' at '../schemas/s.xsd'; declare function a:f() { 'a' castable as s:other };");

        outcome.Should().Be("True");
        host.Asked.Where(a => a.Location.EndsWith(".xsd", StringComparison.Ordinal)).Should().HaveCountGreaterThanOrEqualTo(2)
            .And.OnlyContain(a => a.Module == LibraryUri);
    }
}
