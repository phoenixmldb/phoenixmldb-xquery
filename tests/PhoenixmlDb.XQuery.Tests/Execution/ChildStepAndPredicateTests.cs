using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Two shortcuts, and that each gives what the long way gave. A child step from several nodes
/// that are different and in document order keeps no set of what it has seen and sorts only
/// when one of the nodes is inside another. A predicate that is a comparison or a function
/// call is evaluated at once, with no async iterator.
/// </summary>
public class ChildStepAndPredicateTests
{
    private readonly XQueryFacade _facade = new();

    private const string Doc =
        "let $d := document { <r><a id='1'><c>1</c><a id='2'><c>2</c></a><c>3</c></a><b id='3'><c>4</c></b><a id='4'/></r> } return ";

    [Theory]
    // parents in document order, none inside another
    [InlineData("string-join($d/r/*/c, ',')", "1,3,4")]
    [InlineData("string-join($d/r/(a | b)/c, ',')", "1,3,4")]
    // one parent inside another: the children interleave, and come out in document order
    [InlineData("string-join($d//a/c, ',')", "1,2,3")]
    [InlineData("string-join($d//a/node()/local-name(), ',')", "c,a,c,c")]
    [InlineData("string-join(($d//a | $d//b)/c, ',')", "1,2,3,4")]
    // parents not in document order, and the same parent twice
    [InlineData("string-join(($d//b, $d//a)/c, ',')", "1,2,3,4")]
    [InlineData("string-join(reverse($d//a)/c, ',')", "1,2,3")]
    [InlineData("count(($d/r/b, $d/r/b)/c)", "1")]
    [InlineData("count(($d/r/a, $d/r/b, $d/r/a)/c)", "3")]
    // a parent with no children, a document node, a node that is not an element
    [InlineData("count($d/r/a[@id = '4']/*)", "0")]
    [InlineData("count(($d, $d/r)/*)", "4")]
    [InlineData("count($d//c/text()/*)", "0")]
    // the node test still applies
    [InlineData("string-join($d/r/*/a/@id, ',')", "2")]
    [InlineData("count($d/r/*/text())", "0")]
    public async Task A_child_step_from_several_nodes(string query, string expected)
        => (await _facade.EvaluateAsync(Doc + query)).Should().Be(expected);

    [Theory]
    // a comparison and a function call as the predicate
    [InlineData("string-join($d//a[c = '2']/@id, ',')", "2")]
    [InlineData("string-join($d//*[c = ('1', '4')]/@id, ',')", "1,3")]
    [InlineData("string-join($d//*[count(c) = 2]/@id, ',')", "1")]
    [InlineData("string-join($d//*[exists(c)]/@id, ',')", "1,2,3")]
    [InlineData("string-join($d//*[empty(c) and @id]/@id, ',')", "4")]
    [InlineData("string-join($d//c[. > 2], ',')", "3,4")]
    // a function call that gives a number selects by position
    [InlineData("string-join((10, 20, 30)[abs(-2)] ! string(), ',')", "20")]
    [InlineData("string-join((10, 20, 30)[count($d//b) + 2] ! string(), ',')", "30")]
    // one that gives nothing selects nothing; one that gives a node selects all
    [InlineData("count((10, 20, 30)[head(())])", "0")]
    [InlineData("count((10, 20, 30)[head($d//b)])", "3")]
    [InlineData("count((10, 20, 30)[tail($d//a)])", "3")]
    public async Task A_predicate_that_is_a_comparison_or_a_call(string query, string expected)
        => (await _facade.EvaluateAsync(Doc + query)).Should().Be(expected);

    [Fact]
    public async Task A_call_that_gives_two_values_that_are_not_nodes_is_FORG0006()
    {
        var act = () => _facade.EvaluateAsync("(10, 20, 30)[tokenize('a b')]");
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("Effective boolean value");
    }
}
