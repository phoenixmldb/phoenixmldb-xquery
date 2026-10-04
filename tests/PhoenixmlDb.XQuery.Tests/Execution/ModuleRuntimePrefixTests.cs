using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A name a library function resolves at RUN time (format-number's format name, xs:QName('p:x'),
/// a computed element name) uses the prefixes of the module that declares the function.
/// </summary>
/// <remarks>
/// They were resolved against the MAIN module's bindings. A prefix only the library binds failed
/// (FONS0004, FODF1280); one both bind silently took the importing query's namespace. QT3
/// fn-load-xquery-module-040 names a module-local decimal format "local:de", where `local` is the
/// module's own prefix.
/// </remarks>
public sealed class ModuleRuntimePrefixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-mrp-" + Guid.NewGuid().ToString("N"));

    public ModuleRuntimePrefixTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string Module = """
        module namespace local = "urn:m";
        declare namespace p = "urn:p";
        declare decimal-format local:de decimal-separator = "," grouping-separator = ".";
        declare function local:qname() { namespace-uri-from-QName(xs:QName("p:x")) };
        declare function local:element() { namespace-uri(element {"p:y"} {}) };
        declare function local:format($n) { format-number($n, "#.###,##", "local:de") };
        declare function local:direct() { <a>{1}</a> };
        """;

    private async Task<string> RunAsync(string query)
    {
        var path = Path.Combine(_dir, "m.xqm");
        await File.WriteAllTextAsync(path, Module);
        var engine = new QueryEngine(nodeProvider: new XdmDocumentStore());
        var compiled = engine.Compile("import module namespace m = 'urn:m'; " + query, new CompilationOptions
        {
            ExternalModules = new Dictionary<string, List<string>> { ["urn:m"] = [path] },
        });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var results = new List<string>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            results.Add(item?.ToString() ?? "");
        return string.Join(" ", results);
    }

    [Fact]
    public async Task XsQName_UsesTheModulesPrefix()
        => (await RunAsync("m:qname()")).Should().Be("urn:p");

    [Fact]
    public async Task XsQName_IgnoresTheImportersBindingOfTheSamePrefix()
        => (await RunAsync("declare namespace p = 'urn:main'; (m:qname(), namespace-uri-from-QName(xs:QName('p:x')))"))
            .Should().Be("urn:p urn:main");

    [Fact]
    public async Task ComputedElementName_UsesTheModulesPrefix()
        => (await RunAsync("m:element()")).Should().Be("urn:p");

    [Fact]
    public async Task DecimalFormatNamedByString_UsesTheModulesPrefix()
        => (await RunAsync("m:format(1234567.761)")).Should().Be("1.234.567,76");

    // The module's prolog prefixes are not in-scope namespaces of what its functions construct.
    [Fact]
    public async Task ConstructedElement_DoesNotDeclareTheModulesPrologPrefixes()
        => (await RunAsync("serialize(m:direct())")).Should().Be("<a>1</a>");

    [Theory]
    [InlineData("93.7", "FOQM0006")]
    [InlineData("4.0", "ok")]
    public async Task LoadXQueryModule_UnsupportedVersion_IsFOQM0006(string version, string expected)
    {
        string outcome;
        try { await RunAsync($"load-xquery-module('urn:m', map{{'xquery-version': {version}}}) ! 'ok'"); outcome = "ok"; }
        catch (XQueryRuntimeException e) { outcome = e.ErrorCode; }
        outcome.Should().Be(expected);
    }
}
