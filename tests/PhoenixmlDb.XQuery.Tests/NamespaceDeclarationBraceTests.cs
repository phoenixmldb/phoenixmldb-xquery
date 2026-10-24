using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A namespace declaration attribute must be a literal (XQST0022), and {{ and }} are escaped
/// literal braces, not enclosed expressions. A value with any brace was split into parts and
/// rejected as if it held an expression (QT3 K2-DirectConElemNamespace-59, -65, -75, -76).
/// </summary>
public class NamespaceDeclarationBraceTests
{
    private static async Task<string> Eval(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("""namespace-uri(<p:e xmlns:p="urn:a{{b}}c"/>)""", "urn:a{b}c")]
    [InlineData("""namespace-uri(<e xmlns="urn:{{1}}"/>)""", "urn:{1}")]
    [InlineData("""namespace-uri-for-prefix("p", <e xmlns:p='urn:{{}}{{{{}}}}'/>)""", "urn:{}{{}}")]
    public async Task Escaped_braces_are_literal_characters(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Theory]
    [InlineData("""<e xmlns:p="{'urn:a'}"/>""")]
    [InlineData("""<e xmlns="urn:{1}"/>""")]
    public void An_enclosed_expression_is_XQST0022(string query)
    {
        var act = () => new QueryEngine().Compile(query);
        act.Should().Throw<PhoenixmlDb.XQuery.Parser.XQueryParseException>().WithMessage("*XQST0022*");
    }
}
