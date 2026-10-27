using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A computed constructor whose name is a QName written in the query resolves its prefix against
/// the statically known namespaces: an undeclared prefix is XPST0081. Only a name computed from
/// an expression is XQDY0074 (or XQDY0044 for xmlns). Both raised the dynamic code (QT3
/// Constr-compattr-name-3, Constr-compelem-name-3, namespaceDecl-2, nscons-024, -025,
/// K2-ComputeConAttr-37, -39).
/// </summary>
public class LiteralConstructorNamePrefixTests
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

    private static async Task<string> ErrorCode(string query)
    {
        var act = () => Eval(query);
        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        return thrown switch
        {
            PhoenixmlDb.XQuery.Functions.XQueryException q => q.ErrorCode,
            XQueryRuntimeException r => r.ErrorCode,
            _ => thrown.GetType().Name,
        };
    }

    [Theory]
    [InlineData("element e { attribute foo:a { 'x' } }", "XPST0081")]
    [InlineData("element foo:e { 'x' }", "XPST0081")]
    [InlineData("attribute xmlns:a { 'x' }", "XPST0081")]
    [InlineData("<e>{ namespace z { 'urn:z' }, element z:e {} }</e>", "XPST0081")]
    [InlineData("element e { attribute { 'foo:a' } { 'x' } }", "XQDY0074")]
    [InlineData("element { 'foo:e' } { 'x' }", "XQDY0074")]
    [InlineData("attribute { 'xmlns:a' } { 'x' }", "XQDY0044")]
    public async Task An_undeclared_prefix_raises_the_code_for_how_the_name_was_written(string query, string expected)
        => (await ErrorCode(query)).Should().Be(expected);

    [Theory]
    [InlineData("<e xmlns:foo='urn:f'>{ element foo:c { attribute foo:a { 1 } } }</e>/*/@*/namespace-uri()", "urn:f")]
    [InlineData("declare namespace foo = 'urn:p'; namespace-uri(element foo:e {})", "urn:p")]
    public async Task A_declared_prefix_still_resolves(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
