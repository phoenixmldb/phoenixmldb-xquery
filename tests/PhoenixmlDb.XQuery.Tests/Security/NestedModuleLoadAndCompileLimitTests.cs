using System.Diagnostics;
using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// Two bounds a host running untrusted queries relies on. A module that loads a module at run
/// time (itself, most simply) nested one evaluation inside another with no limit, and
/// overflowed the stack, which ends the process. And an <c>import schema</c> is loaded while
/// the query is compiled, before any execution limit exists, so a schema whose own values
/// backtrack catastrophically against its patterns held the compile for as long as it liked.
/// </summary>
public sealed class NestedModuleLoadAndCompileLimitTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-nml-" + Guid.NewGuid().ToString("N"));

    public NestedModuleLoadAndCompileLimitTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Module = """
        module namespace m = "urn:m";
        declare function m:down($n as xs:integer) as xs:integer {
          if ($n = 0) then 0
          else load-xquery-module('urn:m', map { 'location-hints': 'M_URI' })?functions(QName('urn:m', 'down'))?1($n - 1)
        };
        """;

    private const string SelfLoadingModule = """
        module namespace s = "urn:s";
        declare variable $s:v := load-xquery-module('urn:s')?variables(QName('urn:s', 'v'));
        """;

    private async Task<string> RunAsync(string query)
    {
        var m = Path.Combine(_dir, "m.xqm");
        var s = Path.Combine(_dir, "s.xqm");
        // By location: the host's module map is in scope only while a module is being loaded.
        await File.WriteAllTextAsync(m, Module.Replace("M_URI", new Uri(m).AbsoluteUri, StringComparison.Ordinal));
        await File.WriteAllTextAsync(s, SelfLoadingModule);
        var engine = new QueryEngine();
        var compiled = engine.Compile(query, new CompilationOptions
        {
            ExternalModules = new Dictionary<string, List<string>> { ["urn:m"] = [m], ["urn:s"] = [s] },
        });
        if (!compiled.Success)
            return string.Join("; ", compiled.Errors.Select(e => $"{e.Code}: {e.Message}"));
        var results = new List<object?>();
        try
        {
            await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                results.Add(item);
        }
        catch (XQueryRuntimeException ex)
        {
            return ex.ErrorCode;
        }
        return string.Join(",", results);
    }

    private const string Down = "load-xquery-module('urn:m')?functions(QName('urn:m', 'down'))?1";

    [Fact]
    public async Task A_few_nested_loads_work() =>
        (await RunAsync(Down + "(10)")).Should().Be("0");

    [Fact]
    public async Task Loads_nested_past_the_cap_are_an_error_not_a_stack_overflow() =>
        (await RunAsync(Down + "(100000)")).Should().Be("FOQM0003");

    [Fact]
    public async Task A_module_that_loads_itself_while_loading_is_an_error_not_a_stack_overflow() =>
        (await RunAsync("load-xquery-module('urn:s')?variables(QName('urn:s', 'v'))")).Should().Be("FOQM0003");

    /// <remarks>
    /// The schema would take hours to compile with no limit: the default value is forty
    /// characters, and the pattern backtracks two ways at each one. So the test does not time
    /// the compilation against a figure that depends on the machine (a bound of 3 s failed at
    /// 4.4 s on a Windows CI runner). It asserts that compilation ends at all, under a watchdog
    /// that is far from both the bounded and the unbounded time, and that it ends because of
    /// the limit.
    /// </remarks>
    [Fact]
    public async Task An_imported_schema_is_bounded_while_the_query_compiles()
    {
        var value = new string('a', 40);
        var xsd = Path.Combine(_dir, "slow.xsd");
        await File.WriteAllTextAsync(xsd, $"""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:c" xmlns="urn:c">
              <xs:simpleType name="slow"><xs:restriction base="xs:string"><xs:pattern value="(a+)+b"/></xs:restriction></xs:simpleType>
              <xs:element name="e" type="slow" default="{value}"/>
            </xs:schema>
            """);
        var query = $"import schema namespace c = 'urn:c' at '{new Uri(xsd).AbsoluteUri}'; 1";
        var compilation = Task.Run(() => new QueryEngine().Compile(query,
            new CompilationOptions { RegexMatchTimeout = TimeSpan.FromMilliseconds(300) }));
        var finished = await Task.WhenAny(compilation, Task.Delay(TimeSpan.FromMinutes(2)));

        finished.Should().BeSameAs(compilation, "the match limit ends the compilation; with no limit it runs for hours");
        var compiled = await compilation;
        compiled.Success.Should().BeFalse();
        compiled.Errors.Select(e => e.Code).Should().Contain("XQST0059");
        compiled.Errors.Select(e => e.Message).Should().Contain(m => m.Contains("match time limit", StringComparison.Ordinal));
    }
}
