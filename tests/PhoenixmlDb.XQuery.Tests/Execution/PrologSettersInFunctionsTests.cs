using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A module's `declare boundary-space` and `declare default order empty` govern the expressions
/// written in that module, function bodies included (XQuery 3.1 §4.3, §4.7).
/// </summary>
/// <remarks>
/// Function bodies are planned later, in a fresh context, so they took the strip policy whatever
/// the prolog said. And both module kinds built their function declarations before reading the
/// default order, so a function's `order by` sorted empty least. A library module's settings were
/// lost the same two ways (QT3 fn-load-xquery-module-042 to 046).
/// </remarks>
public sealed class PrologSettersInFunctionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-psf-" + Guid.NewGuid().ToString("N"));

    public PrologSettersInFunctionsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // Three positions over (1, 2): the third key is empty, so it sorts last only under "empty greatest".
    private const string OrderBody = "for $i in 1 to 3 let $x := subsequence((1, 2), $i, 1) order by $x return count($x)";

    private static async Task<string> RunAsync(string query, Dictionary<string, List<string>>? modules = null)
    {
        var engine = new QueryEngine(nodeProvider: new XdmDocumentStore());
        var compiled = engine.Compile(query, new CompilationOptions { ExternalModules = modules });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var results = new List<string>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            results.Add(item?.ToString() ?? "");
        return string.Join(" ", results);
    }

    private async Task<string> RunWithModuleAsync(string module, string call)
    {
        var path = Path.Combine(_dir, "m.xqm");
        await File.WriteAllTextAsync(path, module);
        return await RunAsync("import module namespace m = 'urn:m'; " + call,
            new Dictionary<string, List<string>> { ["urn:m"] = [path] });
    }

    [Fact]
    public async Task BoundarySpacePreserve_ReachesAFunctionBody()
        => (await RunAsync("declare boundary-space preserve; declare function local:f() { <a> {1} </a> }; serialize(local:f())"))
            .Should().Be("<a> 1 </a>");

    [Fact]
    public async Task BoundarySpacePreserve_ReachesAnInlineFunction()
        => (await RunAsync("declare boundary-space preserve; let $f := function() { <a> {1} </a> } return serialize($f())"))
            .Should().Be("<a> 1 </a>");

    [Fact]
    public async Task BoundarySpace_StillStripsByDefault()
        => (await RunAsync("declare function local:f() { <a> {1} </a> }; serialize(local:f())")).Should().Be("<a>1</a>");

    [Fact]
    public async Task DefaultOrderEmptyGreatest_ReachesAFunctionBody()
        => (await RunAsync($"declare default order empty greatest; declare function local:f() {{ {OrderBody} }}; local:f()"))
            .Should().Be("1 1 0");

    [Fact]
    public async Task LibraryModule_BoundarySpace_IsItsOwn()
        => (await RunWithModuleAsync(
                "module namespace m = 'urn:m'; declare boundary-space preserve; declare function m:f() { <a> {1} </a> };",
                "serialize(m:f())"))
            .Should().Be("<a> 1 </a>");

    [Fact]
    public async Task LibraryModule_DefaultOrder_IsItsOwn_AndDoesNotLeak()
        => (await RunWithModuleAsync(
                $"module namespace m = 'urn:m'; declare default order empty greatest; declare function m:f() {{ {OrderBody} }};",
                $"(m:f(), {OrderBody})"))
            .Should().Be("1 1 0 0 1 1");
}
