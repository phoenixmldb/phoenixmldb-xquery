using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// In XPath 1.0 compatibility mode, = and != with a boolean operand compare effective boolean
/// values, and an empty node-set is false() (XPath 1.0 §3.4): $empty = false() is true. The rule
/// that an empty operand makes a general comparison false ran first, so it was false (W3C XSLT
/// backwards-045). Outside compatibility mode an empty operand still gives false. Every expected
/// value is Saxon-HE 12.10's.
/// </summary>
public sealed class CompatibilityEmptyVersusBooleanTests
{
    private static async Task<object?> EvalAsync(string query, bool compatible)
    {
        var store = new PhoenixmlDb.XQuery.XdmDocumentStore();
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => $"{e.Code} {e.Message}")));
        var ctx = engine.CreateContext();
        ctx.BackwardsCompatible = compatible;
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(i);
        return items.Single();
    }

    private const string Empty = "(<r><a/></r>)/j";

    [Theory]
    [InlineData($"{Empty} = false()", true, true)]
    [InlineData($"{Empty} != false()", true, false)]
    [InlineData($"{Empty} = true()", true, false)]
    [InlineData($"{Empty} != true()", true, true)]
    [InlineData($"false() = {Empty}", true, true)]
    [InlineData("(<r><a/></r>)/a = true()", true, true)]
    [InlineData($"{Empty} = 'x'", true, false)]
    [InlineData($"{Empty} = false()", false, false)]
    [InlineData($"{Empty} != true()", false, false)]
    public async Task An_empty_node_set_compared_with_a_boolean(string query, bool compatible, bool expected)
        => (await EvalAsync(query, compatible)).Should().Be(expected);
}
