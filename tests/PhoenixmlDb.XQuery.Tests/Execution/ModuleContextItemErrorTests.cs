using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A library module's context item: it may not be given a value (XQST0113, QT3
/// contextDecl-048/052), and loading one whose initialisers read it without supplying one is
/// FOQM0006 (QT3 fn-load-xquery-module-909/910). Both surfaced as "context item is absent".
/// </summary>
public sealed class ModuleContextItemErrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-mci-" + Guid.NewGuid().ToString("N"));

    public ModuleContextItemErrorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private async Task<string> RunAsync(string module, string query)
    {
        var path = Path.Combine(_dir, "m.xqm");
        await File.WriteAllTextAsync(path, module);
        var engine = new QueryEngine();
        try
        {
            var compiled = engine.Compile(query, new CompilationOptions
            {
                ExternalModules = new Dictionary<string, List<string>> { ["urn:m"] = [path] },
            });
            if (!compiled.Success)
                return compiled.Errors[0].Code ?? compiled.Errors[0].Message;
            var results = new List<object?>();
            await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                results.Add(item);
            return string.Join(",", results);
        }
        catch (XQueryRuntimeException e) { return e.ErrorCode; }
        catch (XQueryParseException e) { return e.Message.Split(':')[0]; }
    }

    [Theory]
    [InlineData("declare context item := 17;")]
    [InlineData("declare context item as xs:integer := 17;")]
    [InlineData("declare context item external := 17;")]
    public async Task A_library_module_may_not_give_the_context_item_a_value(string decl) =>
        (await RunAsync($"module namespace m = \"urn:m\"; {decl}",
            "import module namespace m = \"urn:m\"; 1")).Should().Contain("XQST0113");

    [Fact]
    public async Task A_library_module_may_declare_its_context_item_type() =>
        (await RunAsync("module namespace m = \"urn:m\"; declare context item as xs:integer external;",
            "import module namespace m = \"urn:m\"; 1")).Should().Be("1");

    private const string ReadsContext = """
        module namespace m = "urn:m";
        declare variable $m:context := .;
        declare function m:ctx() { $m:context };
        """;

    [Fact]
    public async Task Loading_a_module_that_reads_an_unsupplied_context_item_is_FOQM0006() =>
        (await RunAsync(ReadsContext, "load-xquery-module('urn:m') => map:size()")).Should().Be("FOQM0006");

    [Fact]
    public async Task Supplying_the_context_item_loads_the_module() =>
        (await RunAsync(ReadsContext,
            "load-xquery-module('urn:m', map{'context-item': 42})?functions(QName('urn:m','ctx'))?0()")).Should().Be("42");

    [Theory]
    [InlineData("declare context item external; 1", "1")]
    [InlineData("declare context item as xs:double external; . = 17", "XPDY0002")]
    public async Task An_unsupplied_context_item_is_an_error_only_where_it_is_read(string query, string expected) =>
        (await RunAsync("module namespace m = \"urn:m\";", query)).Should().Be(expected);
}
