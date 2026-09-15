using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Every PhoeniXML extension function lives in https://schemas.phoenixml.dev/2026/functions, which the library
/// predeclares as phx (namespace-consolidation design, 2026-09-15). The retired spellings, ft: in
/// http://www.w3.org/2007/xpath-full-text and dbxml:metadata in https://schemas.phoenixml.dev/2026/db, are a clean
/// break with no aliases. Host bindings (<see cref="CompilationOptions.StaticNamespaces"/>) may add a prefix but not
/// rebind a predeclared one, phx included.
/// </summary>
public sealed class ExtensionNamespaceTests
{
    private const string PhxUri = "https://schemas.phoenixml.dev/2026/functions";
    private const string RetiredDbUri = "https://schemas.phoenixml.dev/2026/db";
    private const string RetiredFtUri = "http://www.w3.org/2007/xpath-full-text";

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

    private static string Describe(QueryCompilationResult result)
        => string.Join("; ", result.Errors.Select(e => $"{e.Code} {e.Message}"));

    [Fact]
    public void AllSixExtensionFunctions_AreInThePhxNamespace_AndNoneRemainInTheRetiredIds()
    {
        var functions = FunctionLibrary.Standard.GetAllFunctions().ToList();
        functions.Where(f => f.Name.Namespace == FunctionNamespaces.Phx).Select(f => f.Name.LocalName).Distinct()
            .Should().BeEquivalentTo("metadata", "stem", "tokenize", "score", "is-stop-word", "thesaurus-lookup");
        functions.Should().NotContain(f => f.Name.Namespace.Value == 9 || f.Name.Namespace.Value == 10);
    }

    [Theory]
    [InlineData("phx:stem('running')")]
    [InlineData("phx:tokenize('alpha beta')")]
    [InlineData("phx:metadata(/, 'status')")]
    [InlineData("phx:metadata(/)")]
    [InlineData($"Q{{{PhxUri}}}stem('running')")]
    public void PhxFunctions_CompileWithNoProlog(string query)
    {
        var compiled = Compile(query);
        compiled.Success.Should().BeTrue(Describe(compiled));
    }

    [Fact]
    public async Task PhxFunctions_RunWithNoProlog()
        => (await EvalAsync("phx:tokenize('alpha beta')")).Should().NotBeEmpty();

    [Fact]
    public async Task PhxFunctions_RunThroughTheFacade()
        => (await new XQueryFacade().EvaluateAsync("phx:tokenize('alpha beta')")).Should().Contain("alpha");

    [Fact]
    public async Task ThePhxPrefix_IsBoundAtRunTime()
        => (await EvalAsync("string(namespace-uri-from-QName(xs:QName('phx:x')))")).Should().Equal(PhxUri);

    [Fact]
    public async Task APrologDeclaration_OverridesThePhxPrefix()
    {
        (await EvalAsync("declare namespace phx = \"urn:mine\"; namespace-uri(<phx:x/>)")).Should().Equal("urn:mine");
        var compiled = Compile("declare namespace phx = \"urn:mine\"; phx:stem('x')");
        compiled.Errors.Should().Contain(e => e.Code == "XPST0017", Describe(compiled));
    }

    [Fact]
    public void AHostBinding_CannotRebindPhx()
    {
        var compiled = Compile("1", new Dictionary<string, string> { ["phx"] = "urn:not-phx" });
        compiled.Success.Should().BeFalse();
        compiled.Errors.Should().ContainSingle().Which.Message.Should().Contain("phx");
    }

    [Fact]
    public void AHostBinding_OfPhxToItsOwnUri_IsANoOp()
        => Compile("phx:stem('x')", new Dictionary<string, string> { ["phx"] = PhxUri }).Success.Should().BeTrue();

    [Fact]
    public void AQuery_CannotDeclareAPhxFunction()
    {
        var compiled = Compile("declare function phx:f() { 1 }; 1");
        compiled.Errors.Should().Contain(e => e.Code == "XQST0045", Describe(compiled));
    }

    [Theory]
    [InlineData("ft:stem('x')", "XPST0081")]
    [InlineData("dbxml:metadata(/, 'x')", "XPST0081")]
    [InlineData($"declare namespace ft = \"{RetiredFtUri}\"; ft:stem('x')", "XPST0017")]
    [InlineData($"declare namespace dbxml = \"{RetiredDbUri}\"; dbxml:metadata(/, 'x')", "XPST0017")]
    [InlineData($"Q{{{RetiredFtUri}}}stem('x')", "XPST0017")]
    [InlineData($"Q{{{RetiredDbUri}}}metadata(/, 'x')", "XPST0017")]
    public void RetiredSpellings_FailToCompile(string query, string code)
    {
        var compiled = Compile(query);
        compiled.Success.Should().BeFalse();
        compiled.Errors.Should().Contain(e => e.Code == code, Describe(compiled));
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
    [InlineData("phx", "urn:not-phx")]
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
