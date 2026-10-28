using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// A schema-aware processor gives fn:analyze-string's result the types of the schema for it
/// (F&amp;O 3.1 §5.6.5, C.1). It was always untyped (QT3 analyzeString-020, -021, -024, -025).
/// </summary>
public class AnalyzeStringTypedResultTests
{
    private static async Task<string> Eval(string query)
    {
        var engine = new QueryEngine(nodeProvider: new XdmDocumentStore());
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("(analyze-string('banana', '(b)(anana)')//@nr)[1] instance of attribute(nr, xs:positiveInteger)", "true")]
    [InlineData("analyze-string('banana', '(b)(anana)') instance of element(*, xs:untyped)", "false")]
    [InlineData("data(analyze-string('banana', 'a')/fn:non-match[1]) instance of xs:string", "true")]
    public async Task The_result_is_typed(string query, string expected)
        => (await Eval($"declare namespace fn = 'http://www.w3.org/2005/xpath-functions'; string({query})")).Should().Be(expected);

    [Fact]
    public async Task The_result_matches_the_schema_element_when_the_fn_schema_is_imported()
        => (await Eval("import schema namespace fn = 'http://www.w3.org/2005/xpath-functions'; "
            + "string(analyze-string('banana', '(b)(anana)') instance of schema-element(fn:analyze-string-result)"
            + " and analyze-string('banana', '(b)(anana)')/fn:match[1] instance of schema-element(fn:match))")).Should().Be("true");

    [Theory]
    [InlineData("string(analyze-string('banana', '(b)(an)'))", "banana")]
    [InlineData("string-join(analyze-string('a1b22', '\\d+')/fn:match, '|')", "1|22")]
    [InlineData("count(analyze-string('', 'x')/node())", "0")]
    [InlineData("string-to-codepoints(analyze-string(concat('Mary', codepoints-to-string(13), 'Jones'), 'y.J')/fn:non-match)[5]", "13")]
    [InlineData("string-to-codepoints(analyze-string(concat('Mary', codepoints-to-string(13), 'Jones'), 'y.J', 's')/fn:match)[2]", "13")]
    public async Task The_content_is_unchanged(string query, string expected)
        => (await Eval($"declare namespace fn = 'http://www.w3.org/2005/xpath-functions'; {query}")).Should().Be(expected);
}
