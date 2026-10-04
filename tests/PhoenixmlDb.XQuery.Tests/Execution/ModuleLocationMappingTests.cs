using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A host's ExternalModuleLocations maps a module's location to a local file. A relative `at` hint
/// is matched by the URI it resolves to against the base URI, not only as written.
/// </summary>
/// <remarks>
/// Only the hint as written was looked up, so `at "lib.xqm"` under an http:// base URI went to the
/// network although the host had mapped exactly that location (QT3 d1e78807j failed with a 404).
/// The base URI here is a refused local port, so the unfixed lookup fails fast and offline.
/// </remarks>
public sealed class ModuleLocationMappingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-mlm-" + Guid.NewGuid().ToString("N"));

    public ModuleLocationMappingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string BaseUri = "http://127.0.0.1:9/modules/";

    private async Task<string> RunAsync(string query, string mappedLocation)
    {
        var path = Path.Combine(_dir, "lib.xqm");
        await File.WriteAllTextAsync(path, "module namespace m = 'urn:m'; declare function m:f() { 'from the mapped file' };");
        var engine = new QueryEngine();
        var compiled = engine.Compile(query, new CompilationOptions
        {
            BaseUri = BaseUri,
            ExternalModuleLocations = new Dictionary<string, string> { [mappedLocation] = path },
        });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var results = new List<string>();
        // load-xquery-module resolves its hints at run time, against the context's base URI.
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(staticBaseUri: BaseUri)))
            results.Add(item?.ToString() ?? "");
        return string.Join(" ", results);
    }

    [Fact]
    public async Task ARelativeHint_IsMatchedByItsResolvedLocation()
        => (await RunAsync("import module namespace m = 'urn:m' at 'lib.xqm'; m:f()", "http://127.0.0.1:9/modules/lib.xqm"))
            .Should().Be("from the mapped file");

    [Fact]
    public async Task AHint_IsStillMatchedAsWritten()
        => (await RunAsync("import module namespace m = 'urn:m' at 'lib.xqm'; m:f()", "lib.xqm"))
            .Should().Be("from the mapped file");

    [Fact]
    public async Task LoadXQueryModule_RelativeHint_IsMatchedByItsResolvedLocation()
        => (await RunAsync(
                "load-xquery-module('urn:m', map { 'location-hints': 'lib.xqm' })?functions?(QName('urn:m', 'f'))?0()",
                "http://127.0.0.1:9/modules/lib.xqm"))
            .Should().Be("from the mapped file");
}
