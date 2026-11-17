using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Serialization 3.1 §2, sequence normalization: with no item-separator, adjacent atomic values
/// are joined by one space, and nothing is written next to a node. The xml, html and xhtml
/// methods wrote nothing between items, so ("a", "b") serialized as "ab". Expected values are
/// Saxon-HE 12.10's.
/// </summary>
public sealed class SerializeSequenceNormalizationTests
{
    private static async Task<string> EvalAsync(string query)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return (string)items.Single()!;
    }

    [Theory]
    [InlineData("""serialize(("a", "b"), map{"method": "xml"})""", "a b")]
    [InlineData("""serialize(("a", <x/>, "b", 1, 2), map{"method": "xml"})""", "a<x/>b 1 2")]
    [InlineData("""serialize((true(), true()), map{"method": "html"})""", "true true")]
    [InlineData("""serialize((1, 2), map{"method": "xhtml"})""", "1 2")]
    [InlineData("""serialize(("a", "b"), map{"method": "xml", "item-separator": "|"})""", "a|b")]
    public async Task Adjacent_atomic_values_are_separated_by_a_space(string query, string expected)
        => (await EvalAsync(query)).Should().Be(expected);
}
