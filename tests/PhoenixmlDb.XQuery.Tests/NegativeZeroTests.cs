using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// xs:double has a negative zero and xs:decimal does not. A .NET decimal keeps the sign of zero,
/// so xs:decimal("-0") * -1e0 was +0 (QT3 orderBy29, whose xs:decimal data holds "-0"). The
/// optimizer folded 0 * -1e0 to the integer 0, losing both the type and the sign.
/// </summary>
public class NegativeZeroTests
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
    [InlineData("string(xs:decimal('-0') * -1e0)", "-0")]
    [InlineData("string(xs:decimal('-0.0') * 1e0)", "0")]
    [InlineData("string(-xs:decimal('0') * 1e0)", "0")]
    [InlineData("string((0.0 * -1.0) * 1e0)", "0")]
    [InlineData("string(xs:decimal(-0e0) * 1e0)", "0")]
    [InlineData("string(xs:decimal(<a>-0</a>) * -1e0)", "-0")]
    public async Task An_xs_decimal_zero_has_no_sign(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Theory]
    [InlineData("string(0 * -1e0)", "-0")]
    [InlineData("(0 * -1e0) instance of xs:double", "True")]
    [InlineData("(0 * 1.5) instance of xs:decimal", "True")]
    [InlineData("string(-0e0 + 0)", "0")]
    [InlineData("(0 * 2) instance of xs:integer", "True")]
    public async Task Folding_a_zero_keeps_the_type_and_sign(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
