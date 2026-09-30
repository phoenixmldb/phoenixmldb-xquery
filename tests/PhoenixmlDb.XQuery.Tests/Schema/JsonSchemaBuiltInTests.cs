using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// F&amp;O 3.1's schema for the XML representation of JSON is built in: importing the fn
/// namespace needs no location, fn:json-to-xml's validate option is honoured rather than
/// refused, and its types are usable in element(*, T) tests. Schema-defined type names in kind
/// tests are checked after schemas are imported instead of being rejected by the parser.
/// </summary>
public class JsonSchemaBuiltInTests
{
    private const string Fn = "http://www.w3.org/2005/xpath-functions";

    private static async Task<string> Eval(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        if (!compiled.Success)
            throw new InvalidOperationException(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    private static IReadOnlyList<string> CompileErrorCodes(string query) =>
        new PhoenixmlDb.XQuery.Execution.QueryEngine().Compile(query).Errors.Select(e => e.Code).ToList();

    [Fact]
    public async Task The_fn_namespace_imports_without_a_location() =>
        (await Eval($"import schema namespace j = '{Fn}'; count(json-to-xml('[1]')/j:array/j:number)")).Should().Be("1");

    [Fact]
    public async Task The_validate_option_is_honoured() =>
        (await Eval($"declare namespace j = '{Fn}'; string(json-to-xml('[1]', map {{ 'validate': true() }})/j:array/j:number)"))
            .Should().Be("1");

    [Fact]
    public async Task A_json_schema_type_is_usable_in_an_element_test() =>
        CompileErrorCodes($"import schema namespace j = '{Fn}'; <a/> instance of element(*, j:stringType)").Should().BeEmpty();

    [Theory]
    [InlineData($"import schema namespace j = '{Fn}'; <a/> instance of element(*, j:noSuchType)", "XPST0008")]
    [InlineData("declare namespace q = 'urn:not-imported'; <a/> instance of element(*, q:t)", "XPST0008")]
    [InlineData("<a/> instance of element(*, nope:t)", "XPST0081")]
    public void An_undeclared_kind_test_type_is_a_static_error(string query, string code)
    {
        var act = () => CompileErrorCodes(query);
        var codes = new List<string>();
        try { codes.AddRange(act()); }
        catch (PhoenixmlDb.XQuery.Parser.XQueryParseException ex) { codes.Add(ex.Message); }
        codes.Should().Contain(c => c.Contains(code, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("json-to-xml('[\"String\"]', map { 'liberal': 'something' })")]
    [InlineData("json-to-xml('[\"String\"]', map { 'liberal': () })")]
    public async Task A_non_boolean_liberal_is_XPTY0004(string query)
    {
        var act = () => Eval(query);
        var ex = (await act.Should().ThrowAsync<Exception>()).Which;
        ex.GetType().GetProperty("ErrorCode")?.GetValue(ex).Should().Be("XPTY0004");
    }
}
