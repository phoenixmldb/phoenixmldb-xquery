using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A function item or map in the content of an element or document constructor is XQTY0105
/// (XQuery 3.1 §3.9.1.3). It was atomized and raised FOTY0013 (QT3 function-item-5). Attribute,
/// text and comment constructors atomize their content, so FOTY0013 is right for them.
/// </summary>
public class FunctionItemContentTests
{
    private static async Task<string> ErrorCode(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext())) { }
        };
        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        return thrown switch
        {
            PhoenixmlDb.XQuery.Functions.XQueryException q => q.ErrorCode,
            XQueryRuntimeException r => r.ErrorCode,
            _ => thrown.GetType().Name,
        };
    }

    [Theory]
    [InlineData("element a { avg#1 }", "XQTY0105")]
    [InlineData("<a>{ avg#1 }</a>", "XQTY0105")]
    [InlineData("<a>{ 1, map { } }</a>", "XQTY0105")]
    [InlineData("<a>{ [1, avg#1] }</a>", "XQTY0105")]
    [InlineData("document { function() { 1 } }", "XQTY0105")]
    [InlineData("attribute a { avg#1 }", "FOTY0013")]
    [InlineData("text { avg#1 }", "FOTY0013")]
    public async Task A_function_item_in_constructor_content(string query, string expected)
        => (await ErrorCode(query)).Should().Be(expected);
}
