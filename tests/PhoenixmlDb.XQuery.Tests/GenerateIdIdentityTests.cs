using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:generate-id gives the same NCName for the same node and different ones for different nodes
/// (F&amp;O 3.1 §14.6.4). It was the hash code of the node object, so different nodes could share
/// an id: with many nodes some always did.
/// </summary>
public sealed class GenerateIdIdentityTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task Different_nodes_have_different_ids()
    {
        var result = await _facade.EvaluateAsync(
            "let $d := parse-xml('<r>' || string-join((1 to 100000) ! '<e a=\"1\"/>') || '</r>') "
            + "let $n := ($d//e, $d//@a) return count(distinct-values($n ! generate-id(.))) = count($n)");
        result.Should().Be("true");
    }

    [Fact]
    public async Task The_same_node_has_the_same_id_by_any_path()
    {
        var result = await _facade.EvaluateAsync(
            "let $d := parse-xml('<r><e a=\"1\"/><e a=\"2\"/></r>') return string-join("
            + "(generate-id($d/r/e[2]) eq generate-id(($d//e)[2]), generate-id($d/r/e[1]) eq generate-id($d/r/e[2]), "
            + "every $x in ($d, $d//node(), $d//@*) satisfies generate-id($x) castable as xs:NCName) ! string(.), ' ')");
        result.Should().Be("true false true");
    }
}
