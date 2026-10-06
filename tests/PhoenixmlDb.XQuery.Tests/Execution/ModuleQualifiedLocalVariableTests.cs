using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Variables with a namespace-qualified name that are bound INSIDE a library module's code: the
/// implicit <c>$err:*</c> of a catch clause, and a prefixed <c>let</c>. The rule that hides other
/// modules' prolog variables was applied to them too, so <c>$err:code</c> in a library module's
/// catch was "Variable $code is not defined"; and once past that, the catch variables were bound
/// under a different namespace id than the one the module's references carried.
/// </summary>
public sealed class ModuleQualifiedLocalVariableTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-mqv-" + Guid.NewGuid().ToString("N"));

    public ModuleQualifiedLocalVariableTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Module = """
        module namespace m = "urn:m";
        declare namespace p = "urn:p";
        declare %private variable $m:hidden := 1;
        declare function m:code() { try { 1 div 0 } catch * { local-name-from-QName($err:code) } };
        declare function m:described() { try { error(xs:QName('m:boom'), 'bad') } catch * { string($err:code) || '|' || $err:description } };
        declare function m:named() { try { 1 div 0 } catch err:FOAR0001 { 'caught ' || local-name-from-QName($err:code) } };
        declare function m:prefixed-let() { let $p:x := 5 return $p:x + 1 };
        """;

    private async Task<string> RunAsync(string query)
    {
        var path = Path.Combine(_dir, "m.xqm");
        await File.WriteAllTextAsync(path, Module);
        var engine = new QueryEngine();
        var compiled = engine.Compile(query, new CompilationOptions
        {
            ExternalModules = new Dictionary<string, List<string>> { ["urn:m"] = [path] },
        });
        if (!compiled.Success)
            return string.Join("; ", compiled.Errors.Select(e => $"{e.Code}: {e.Message}"));
        var results = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            results.Add(item);
        return string.Join(",", results);
    }

    [Theory]
    [InlineData("m:code()", "FOAR0001")]
    [InlineData("m:described()", "m:boom|bad")]
    [InlineData("m:named()", "caught FOAR0001")]
    [InlineData("m:prefixed-let()", "6")]
    public async Task In_an_imported_module(string call, string expected) =>
        (await RunAsync("import module namespace m = 'urn:m'; " + call)).Should().Be(expected);

    [Theory]
    [InlineData("code", "FOAR0001")]
    [InlineData("described", "m:boom|bad")]
    public async Task In_a_dynamically_loaded_module(string function, string expected) =>
        (await RunAsync($"load-xquery-module('urn:m')?functions(QName('urn:m', '{function}'))?0()")).Should().Be(expected);

    [Fact]
    public async Task Another_modules_private_variable_is_still_hidden_and_named_in_full()
    {
        // The message used to drop the prefix: "Variable $hidden is not defined".
        var result = await RunAsync("import module namespace m = 'urn:m'; $m:hidden");
        result.Should().StartWith("XPST0008");
        result.Should().Contain("$m:hidden");
    }
}
