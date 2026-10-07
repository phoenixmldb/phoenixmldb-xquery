using System.IO;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A module loaded with fn:load-xquery-module runs on the HOST's function library. Its sub-engine
/// was built with the default library, so a host that replaces a file-reading function with a
/// guarded one found the unguarded built-in still reachable from inside any loaded module.
/// </summary>
public sealed class LoadXQueryModuleHostFunctionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"phoenixmldb-hostfn-{Guid.NewGuid():N}");

    public LoadXQueryModuleHostFunctionsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>The host's stand-in for fn:unparsed-text#1: it reads nothing.</summary>
    private sealed class GuardedUnparsedText : XQueryFunction
    {
        public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text");
        public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
        public override IReadOnlyList<FunctionParameterDef> Parameters =>
            [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.ZeroOrMoreItems }];
        public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, PhoenixmlDb.XQuery.Ast.ExecutionContext context)
            => ValueTask.FromResult<object?>("GUARDED");
    }

    private async Task<string> Run(string module, string query)
    {
        var secret = Path.Combine(_dir, "secret.txt");
        await File.WriteAllTextAsync(secret, "SECRET");
        var modulePath = Path.Combine(_dir, "m.xqm");
        await File.WriteAllTextAsync(modulePath, module.Replace("{secret}", new Uri(secret).AbsoluteUri, StringComparison.Ordinal));

        var functions = FunctionLibrary.Standard.Copy();
        functions.Register(new GuardedUnparsedText());
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(functions: functions, nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query.Replace("{module}", new Uri(modulePath).AbsoluteUri, StringComparison.Ordinal));
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var results = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            results.Add(item);
        return string.Join(" ", results);
    }

    private const string Module = """
        module namespace m = "urn:m";
        declare variable $m:v := unparsed-text("{secret}");
        declare function m:f() { unparsed-text("{secret}") };
        """;

    [Fact]
    public async Task HostReplacement_IsWhatTheCallingQueryCalls()
        => (await Run(Module, "unparsed-text('anything')")).Should().Be("GUARDED");

    [Fact]
    public async Task HostReplacement_IsWhatAFunctionOfALoadedModuleCalls()
        => (await Run(Module,
                "load-xquery-module('urn:m', map { 'location-hints': '{module}' })?functions(QName('urn:m', 'f'))?0()"))
            .Should().Be("GUARDED");

    [Fact]
    public async Task HostReplacement_IsWhatAVariableOfALoadedModuleCalls()
        => (await Run(Module,
                "load-xquery-module('urn:m', map { 'location-hints': '{module}' })?variables(QName('urn:m', 'v'))"))
            .Should().Be("GUARDED");

    [Fact]
    public async Task LoadedModule_DoesNotSeeTheCallingQuerysFunctions()
    {
        // The loader hands on the host's library, not the caller's: local:secret is not the module's.
        var act = () => Run("""
                module namespace m = "urn:m";
                declare function m:f() { local:secret() };
                """,
            "declare function local:secret() { 'caller' }; "
            + "load-xquery-module('urn:m', map { 'location-hints': '{module}' })?functions(QName('urn:m', 'f'))?0()");
        await act.Should().ThrowAsync<Exception>();
    }
}
