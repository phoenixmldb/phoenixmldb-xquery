using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A relative module location hint in a query with no base URI is not looked up in the process
/// working directory, with or without a resource policy. <c>import module ... at 'm.xqm'</c>
/// loaded <c>&lt;cwd&gt;/m.xqm</c>: without a policy directly, and under a policy whose file root
/// holds the working directory through the policy's own resolution. A host that wants the
/// working directory gives it as the base URI.
/// </summary>
[Collection(WorkingDirectoryTests.Name)]
public sealed class ModuleHintWithoutBaseTests : IDisposable
{
    private readonly string _name = "phx-mod-" + Guid.NewGuid().ToString("N") + ".xqm";
    private readonly string _path;

    public ModuleHintWithoutBaseTests()
    {
        _path = Path.Combine(Environment.CurrentDirectory, _name);
        File.WriteAllText(_path, """
            module namespace m = "urn:m";
            declare function m:f() { 42 };
            """);
    }

    public void Dispose() => File.Delete(_path);

    private static ResourcePolicy CwdRoot() => ResourcePolicy.CreateBuilder()
        .AllowReadFrom("file", pathPrefix: Environment.CurrentDirectory)
        .AllowImportFrom("file", pathPrefix: Environment.CurrentDirectory)
        .Build();

    private QueryCompilationResult Compile(ResourcePolicy? policy, string? baseUri)
    {
        var engine = new QueryEngine { ResourcePolicy = policy };
        return engine.Compile($"import module namespace m = 'urn:m' at '{_name}'; m:f()",
            new CompilationOptions { BaseUri = baseUri });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_relative_module_hint_with_no_base_is_not_read_from_the_working_directory(bool policy)
    {
        var result = Compile(policy ? CwdRoot() : null, baseUri: null);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "XQST0059");
        result.Errors.Should().NotContain(e => e.Message.Contains(Environment.CurrentDirectory, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_relative_module_hint_resolves_against_the_base_URI(bool policy)
    {
        var baseUri = new Uri(Path.Combine(Environment.CurrentDirectory, "q.xq")).AbsoluteUri;

        var result = Compile(policy ? CwdRoot() : null, baseUri);

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
