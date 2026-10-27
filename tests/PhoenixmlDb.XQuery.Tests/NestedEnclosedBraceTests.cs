using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The lexer adapter must match each '}' with the '{' it closes. One nesting counter, shared by
/// every level, counted the '{' of document { } inside element content and then spent it on the
/// '}' of a deeper enclosed expression, so &lt;doo&gt;{ document { &lt;e&gt;{1}&lt;/e&gt; } }&lt;/doo&gt;
/// failed to parse (QT3 K2-ConDocNode-29).
/// </summary>
public class NestedEnclosedBraceTests
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
    [InlineData("string(<doo>{ document { <e>{ 1 }</e> } }</doo>)", "1")]
    [InlineData("string(<doo>{ map { 'k': <e>{ 2 }</e> }?k }</doo>)", "2")]
    [InlineData("string(<a b='{ map{ 1: 2 }?1 }'>{ ``[x`{ <i>{ 3 }</i> }`y]`` }</a>)", "x3y")]
    [InlineData("string(<a b='{ map{ 1: 2 }?1 }'/>/@b)", "2")]
    [InlineData("``[a`{ map { 1: <b>{ array { 3 }?1 }</b> }?1 }`z]``", "a3z")]
    [InlineData("string(<a>{{{ 4 }}}</a>)", "{4}")]
    public async Task Each_closing_brace_closes_its_own_level(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
