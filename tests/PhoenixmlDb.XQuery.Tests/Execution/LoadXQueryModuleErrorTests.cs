using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// fn:load-xquery-module's errors (F&amp;O 3.1 §17.1.4): FOQM0002 when no module can be found,
/// FOQM0003 when the module has a static error, FOQM0005 when a supplied variable or context
/// item does not match the module's declaration, and XPTY0004 for an option of the wrong type.
/// </summary>
/// <remarks>
/// The underlying analyzer code (XQST0059, XPST0003, the raw XPTY0004) was passed through. It is
/// still in the message; the code a caller can rely on is the one the function defines.
/// </remarks>
public sealed class LoadXQueryModuleErrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-lxe-" + Guid.NewGuid().ToString("N"));

    public LoadXQueryModuleErrorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string Valid = """
        module namespace m = "urn:m";
        declare variable $m:v as xs:string external;
        declare function m:f() { $m:v };
        """;

    private async Task<string> RunAsync(string module, string query)
    {
        var path = Path.Combine(_dir, "m.xqm");
        await File.WriteAllTextAsync(path, module);
        var engine = new QueryEngine();
        var compiled = engine.Compile(query, new CompilationOptions
        {
            ExternalModules = new Dictionary<string, List<string>> { ["urn:m"] = [path] },
        });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        try
        {
            var results = new List<object?>();
            await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                results.Add(item);
            return "ok";
        }
        catch (XQueryRuntimeException e) { return e.ErrorCode; }
    }

    [Fact]
    public async Task UnknownModule_IsFOQM0002()
        => (await RunAsync(Valid, "load-xquery-module('urn:nowhere')")).Should().Be("FOQM0002");

    [Fact]
    public async Task ModuleWithAStaticError_IsFOQM0003()
        => (await RunAsync("module namespace m = \"urn:m\"; declare function m:f() { 1 + };", "load-xquery-module('urn:m')"))
            .Should().Be("FOQM0003");

    [Fact]
    public async Task VariableOfTheWrongType_IsFOQM0005()
        => (await RunAsync(Valid, "load-xquery-module('urn:m', map{'variables': map{QName('urn:m', 'v'): 42}})"))
            .Should().Be("FOQM0005");

    [Theory]
    [InlineData("map{'variables': 'v'}")]
    [InlineData("map{'variables': map{'v': 1}}")]
    [InlineData("map{'vendor-options': 42}")]
    [InlineData("map{'vendor-options': map{'x': 1}}")]
    [InlineData("map{'xquery-version': '3.1'}")]
    public async Task OptionOfTheWrongType_IsXPTY0004(string options)
        => (await RunAsync(Valid, $"load-xquery-module('urn:m', {options})")).Should().Be("XPTY0004");

    [Fact]
    public async Task WellTypedOptions_Load()
        => (await RunAsync(Valid,
                "load-xquery-module('urn:m', map{'xquery-version': 3.1, 'variables': map{QName('urn:m', 'v'): 'x'}})"))
            .Should().Be("ok");
}
