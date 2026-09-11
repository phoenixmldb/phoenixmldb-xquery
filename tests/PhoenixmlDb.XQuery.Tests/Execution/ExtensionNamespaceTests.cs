using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Functions in the engine's extension namespaces (dbxml, ft) were uncallable from query text:
/// a declared prefix minted a fresh namespace id instead of the registered one, and Q{uri}
/// resolved only for the W3C namespaces (xquery#14). And host-supplied namespace bindings
/// (<see cref="CompilationOptions.StaticNamespaces"/>), so an engine can predeclare dbxml.
/// </summary>
public sealed class ExtensionNamespaceTests
{
    private const string DbxmlUri = "https://schemas.phoenixml.dev/2026/db";
    private const string FtUri = "http://www.w3.org/2007/xpath-full-text";

    private static QueryCompilationResult Compile(string query, IReadOnlyDictionary<string, string>? bindings = null)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        return engine.Compile(query, new CompilationOptions { StaticNamespaces = bindings });
    }

    private static async Task<List<object?>> EvalAsync(string query, IReadOnlyDictionary<string, string>? bindings = null)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query, new CompilationOptions { StaticNamespaces = bindings });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return items;
    }

    [Theory]
    [InlineData($"declare namespace x = \"{DbxmlUri}\"; x:metadata(\"status\")")]
    [InlineData($"declare namespace dbxml = \"{DbxmlUri}\"; dbxml:metadata(\"status\")")]
    [InlineData($"Q{{{DbxmlUri}}}metadata(\"status\")")]
    [InlineData($"declare namespace ft = \"{FtUri}\"; ft:stem(\"running\")")]
    [InlineData($"Q{{{FtUri}}}is-stop-word(\"the\")")]
    public void ExtensionFunctions_ResolveByDeclaredPrefixAndByUri(string query)
    {
        var compiled = Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void AHostBinding_LetsAQueryUseThePrefixWithNoProlog()
    {
        Compile("dbxml:metadata(\"status\")").Success.Should().BeFalse("without a binding the prefix is unbound");
        Compile("dbxml:metadata(\"status\")", new Dictionary<string, string> { ["dbxml"] = DbxmlUri })
            .Success.Should().BeTrue();
    }

    [Fact]
    public async Task AHostBinding_AppliesToNames()
        => (await EvalAsync("namespace-uri(<p:x/>)", new Dictionary<string, string> { ["p"] = "urn:host" }))
            .Should().Equal("urn:host");

    [Fact]
    public async Task APrologDeclaration_OverridesAHostBinding()
        => (await EvalAsync("declare namespace p = \"urn:prolog\"; namespace-uri(<p:x/>)",
                new Dictionary<string, string> { ["p"] = "urn:host" }))
            .Should().Equal("urn:prolog");

    /// <summary>
    /// Host bindings reached compile-time resolution only. Everything that resolves a prefix at
    /// RUN time — a QName from a string, a cast to xs:QName, a computed element name — raised
    /// FONS0004 for a host-bound prefix (xquery#21), and a query without a prolog had no runtime
    /// bindings to consult at all.
    /// </summary>
    [Theory]
    [InlineData("string(namespace-uri-from-QName(xs:QName('h:x')))")]
    [InlineData("declare variable $v := 1; string(namespace-uri-from-QName(xs:QName('h:x')))")]
    [InlineData("string(namespace-uri-from-QName('h:x' cast as xs:QName))")]
    [InlineData("namespace-uri(element {'h:x'} {})")]
    [InlineData("declare variable $v := 1; namespace-uri(element {'h:x'} {})")]
    public async Task AHostBinding_ReachesRuntimePrefixResolution(string query)
        => (await EvalAsync(query, new Dictionary<string, string> { ["h"] = "urn:host" }))
            .Should().Equal("urn:host");

    [Fact]
    public async Task AHostBinding_NamesADecimalFormat()
        => (await EvalAsync("declare decimal-format h:df decimal-separator = \"!\"; format-number(1.5, '0!0', 'h:df')",
                new Dictionary<string, string> { ["h"] = "urn:host" }))
            .Should().Equal("1!5");

    [Fact]
    public async Task APrologDeclaration_OverridesAHostBinding_AtRunTimeToo()
        => (await EvalAsync("declare namespace h = \"urn:prolog\"; string(namespace-uri-from-QName(xs:QName('h:x')))",
                new Dictionary<string, string> { ["h"] = "urn:host" }))
            .Should().Equal("urn:prolog");

    [Fact]
    public void BindingAPredeclaredPrefixToItsOwnUri_IsANoOp()
        => Compile("fn:true()", new Dictionary<string, string> { ["fn"] = "http://www.w3.org/2005/xpath-functions" })
            .Success.Should().BeTrue();

    [Theory]
    [InlineData("fn", "urn:not-fn")]
    [InlineData("xs", "urn:not-xs")]
    [InlineData("xml", "urn:not-xml")]
    [InlineData("xmlns", "urn:anything")]
    [InlineData("p", "")]
    [InlineData("not a prefix", "urn:x")]
    public void InvalidBindings_AreCompileErrorsNamingThePrefix(string prefix, string namespaceName)
    {
        var compiled = Compile("1", new Dictionary<string, string> { [prefix] = namespaceName });
        compiled.Success.Should().BeFalse();
        compiled.Errors.Should().ContainSingle().Which.Message.Should().Contain(prefix);
    }
}
