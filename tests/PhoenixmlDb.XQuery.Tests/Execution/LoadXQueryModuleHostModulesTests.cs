using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// fn:load-xquery-module resolves a module the host mapped (CompilationOptions.ExternalModules),
/// as a static `import module` of the same namespace does. It compiled the module with fresh
/// options, so a mapped module "could not be resolved from location hints: []" (QT3
/// fn-load-xquery-module-*, 34 cases).
/// </summary>
public sealed class LoadXQueryModuleHostModulesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-lxm-" + Guid.NewGuid().ToString("N"));

    public LoadXQueryModuleHostModulesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_host_mapped_module_loads_without_location_hints()
    {
        var path = Path.Combine(_dir, "m.xqm");
        await File.WriteAllTextAsync(path, """
            module namespace m = "urn:m";
            declare function m:hello($who as xs:string) as xs:string { "hello " || $who };
            """);
        var engine = new QueryEngine();
        var compiled = engine.Compile(
            """let $m := load-xquery-module("urn:m") return $m?functions(QName("urn:m", "hello"))?1("db")""",
            new CompilationOptions
            {
                ExternalModules = new Dictionary<string, List<string>> { ["urn:m"] = [path] },
            });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var results = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            results.Add(item);
        results.Should().Equal("hello db");
    }
}
