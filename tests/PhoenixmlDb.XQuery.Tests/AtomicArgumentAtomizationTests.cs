using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A node passed to a parameter of an atomic type is atomized by the function conversion rules
/// (XPath 3.1 §3.1.5.2). Built-ins did this one by one, and several did not: escape-html-uri(.),
/// encode-for-uri(.) and iri-to-uri(.) on an element raised XPTY0004 (W3C expression-1601).
/// escape-html-uri and iri-to-uri also encoded each half of a surrogate pair on its own.
/// </summary>
public class AtomicArgumentAtomizationTests
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
    [InlineData("escape-html-uri(<a>x y</a>)", "x y")]
    [InlineData("encode-for-uri(<a>x y</a>)", "x%20y")]
    [InlineData("iri-to-uri(<a>x y</a>)", "x%20y")]
    [InlineData("escape-html-uri(<a b='&#xC6;'/>/@b)", "%C3%86")]
    [InlineData("string-join(<a><b>1</b><b>2</b></a>/b, '-')", "1-2")]
    public async Task A_node_argument_is_atomized(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Theory]
    [InlineData("escape-html-uri('&#x18894;')", "%F0%98%A2%94")]
    [InlineData("iri-to-uri('&#x18894;')", "%F0%98%A2%94")]
    public async Task A_surrogate_pair_is_encoded_as_one_character(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Fact]
    public async Task Two_nodes_for_a_single_atomic_parameter_are_XPTY0004()
    {
        var act = () => Eval("escape-html-uri((<a>x</a>, <a>y</a>))");
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Functions.XQueryException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }
}
