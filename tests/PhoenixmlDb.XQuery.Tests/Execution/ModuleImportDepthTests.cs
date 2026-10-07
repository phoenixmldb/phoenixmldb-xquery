using System.IO;
using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A chain of modules each importing the next is loaded by nested calls, one level per module.
/// Unbounded, a long enough chain overflowed the stack, which cannot be caught and ends the
/// process. The depth is capped and the query fails with a static error instead.
/// </summary>
public sealed class ModuleImportDepthTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"phoenixmldb-depth-{Guid.NewGuid():N}");

    public ModuleImportDepthTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Writes m1 → m2 → … → m{length}; m1's function calls down the whole chain.</summary>
    private async Task<QueryCompilationResult> CompileChain(int length)
    {
        for (var i = 1; i <= length; i++)
        {
            var body = i < length
                ? $"import module namespace n = 'urn:m{i + 1}' at 'm{i + 1}.xqm'; declare function m:f() {{ n:f() + 1 }};"
                : "declare function m:f() { 1 };";
            await File.WriteAllTextAsync(Path.Combine(_dir, $"m{i}.xqm"), $"module namespace m = 'urn:m{i}'; {body}");
        }
        var engine = new QueryEngine();
        return engine.Compile("import module namespace m = 'urn:m1' at 'm1.xqm'; m:f()",
            new CompilationOptions { BaseUri = new Uri(Path.Combine(_dir, "main.xq")).AbsoluteUri });
    }

    [Fact]
    public async Task AChainWithinTheLimit_Loads()
    {
        var compiled = await CompileChain(20);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
    }

    [Fact]
    public async Task AChainPastTheLimit_IsAStaticError_NotAStackOverflow()
    {
        var compiled = await CompileChain(200);
        compiled.Success.Should().BeFalse();
        compiled.Errors.Should().Contain(e => e.Code == "XQST0059" && e.Message.Contains("too deep"));
    }
}
