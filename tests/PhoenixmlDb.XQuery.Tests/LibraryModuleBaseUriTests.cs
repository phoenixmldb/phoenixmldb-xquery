using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A library module has its own static base URI: where it is, or what it declares
/// (XQuery 3.1 §2.1.1). Its functions and variables ran with the importing query's, so a
/// relative URI in a library module resolved against the main module (#223).
/// </summary>
public sealed class LibraryModuleBaseUriTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-libbase-" + Guid.NewGuid().ToString("N"));

    public LibraryModuleBaseUriTests() => Directory.CreateDirectory(Path.Combine(_dir, "lib"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class MemoryResolver : ResourceResolverBase
    {
        public Dictionary<string, string> Content { get; } = new(StringComparer.Ordinal);
        public List<string> Asked { get; } = [];
        public override bool SuppliesAllContent => true;

        public override ResourceContent? ResolveContent(ResourceRequest request)
        {
            var absolute = request.BaseUri != null && Uri.TryCreate(request.BaseUri, request.Location, out var joined)
                ? joined.AbsoluteUri
                : request.Location;
            Asked.Add(absolute);
            return Content.TryGetValue(absolute, out var text) ? new ResourceContent(text, new Uri(absolute)) : null;
        }
    }

    private static readonly Uri MainBase = new("mem://app/main.xq");

    private const string Main = "import module namespace a = 'urn:a' at 'lib/a.xqm'; ";

    private static async Task<string> Run(MemoryResolver host, string query)
    {
        var facade = new XQueryFacade { ResourcePolicy = ResourcePolicy.CreateBuilder().WithResourceResolver(host).Build() };
        return (await facade.EvaluateAsync(query, inputXml: null, baseUri: null, queryBaseUri: MainBase)).Trim();
    }

    [Fact]
    public async Task A_function_in_a_library_module_has_the_modules_base_uri()
    {
        var host = new MemoryResolver();
        host.Content["mem://app/lib/a.xqm"] = "module namespace a = 'urn:a'; declare function a:f() { static-base-uri() };";

        (await Run(host, Main + "a:f() || ' ' || static-base-uri()")).Should().Be("mem://app/lib/a.xqm mem://app/main.xq");
    }

    [Fact]
    public async Task A_variable_in_a_library_module_has_the_modules_base_uri()
    {
        var host = new MemoryResolver();
        host.Content["mem://app/lib/a.xqm"] = "module namespace a = 'urn:a'; declare variable $a:v := static-base-uri();";

        (await Run(host, Main + "string($a:v)")).Should().Be("mem://app/lib/a.xqm");
    }

    [Fact]
    public async Task A_relative_uri_in_a_library_module_is_relative_to_the_module()
    {
        var host = new MemoryResolver();
        host.Content["mem://app/lib/a.xqm"] = "module namespace a = 'urn:a'; declare function a:f() { xs:integer(json-doc('data.json')?n) };";
        host.Content["mem://app/lib/data.json"] = """{ "n": 7 }""";

        (await Run(host, Main + "a:f()")).Should().Be("7");
        host.Asked.Should().Contain("mem://app/lib/data.json").And.NotContain("mem://app/data.json");
    }

    [Fact]
    public async Task A_declared_base_uri_is_relative_to_where_the_module_is()
    {
        var host = new MemoryResolver();
        host.Content["mem://app/lib/a.xqm"] =
            "module namespace a = 'urn:a'; declare base-uri 'data/'; declare function a:f() { static-base-uri() };";
        host.Content["mem://app/lib/b.xqm"] =
            "module namespace b = 'urn:b'; declare base-uri 'http://example.org/x/'; declare function b:f() { static-base-uri() };";

        (await Run(host, Main + "import module namespace b = 'urn:b' at 'lib/b.xqm'; a:f() || ' ' || b:f()"))
            .Should().Be("mem://app/lib/data/ http://example.org/x/");
    }

    [Fact]
    public async Task A_module_that_imports_a_module_keeps_each_base_uri()
    {
        var host = new MemoryResolver();
        host.Content["mem://app/lib/a.xqm"] =
            "module namespace a = 'urn:a'; import module namespace c = 'urn:c' at 'deep/c.xqm'; declare function a:f() { c:f() || ' ' || static-base-uri() };";
        host.Content["mem://app/lib/deep/c.xqm"] = "module namespace c = 'urn:c'; declare function c:f() { static-base-uri() };";

        (await Run(host, Main + "a:f()")).Should().Be("mem://app/lib/deep/c.xqm mem://app/lib/a.xqm");
    }

    [Fact]
    public async Task A_module_read_from_a_file_has_the_files_uri()
    {
        var lib = Path.Combine(_dir, "lib", "a.xqm");
        await File.WriteAllTextAsync(lib, "module namespace a = 'urn:a'; declare function a:f() { static-base-uri() };");
        var mainBase = new Uri(Path.Combine(_dir, "main.xq"));

        var result = await new XQueryFacade().EvaluateAsync(Main + "a:f()", inputXml: null, baseUri: null, queryBaseUri: mainBase);

        result.Trim().Should().Be(new Uri(lib).AbsoluteUri);
    }

    /// <summary>
    /// Under a policy the module's base URI is the location its import named. Here that is
    /// relative and the query has no base URI, so the module has none of its own: the path of
    /// the file read is not given to the query.
    /// </summary>
    [Fact]
    public async Task Under_a_policy_a_module_does_not_learn_the_path_of_the_file_read()
    {
        var lib = Path.Combine(_dir, "lib", "a.xqm");
        await File.WriteAllTextAsync(lib, "module namespace a = 'urn:a'; declare function a:f() { string(static-base-uri()) };");
        var facade = new XQueryFacade { ResourcePolicy = ResourcePolicy.CreateBuilder().AllowImportFrom("file", null, _dir).AllowReadFrom("file", null, _dir).Build() };

        var before = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = _dir;
            var result = await facade.EvaluateAsync(Main + "a:f()");
            result.Should().NotContain(_dir);
        }
        finally
        {
            Environment.CurrentDirectory = before;
        }
    }
}
