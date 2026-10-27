using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// An xs:integer wider than 64 bits is held as a BigInteger. xs:double and xs:float did not take
/// one and raised XPTY0004; every integer casts to both (QT3 K2-NumericMod-7, -8:
/// xs:double(-9223372036854775808)).
/// </summary>
public class WideIntegerToFloatingTests
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
    [InlineData("xs:double(-9223372036854775808) eq -9.223372036854775808e18", "True")]
    [InlineData("xs:double(100000000000000000000) eq 1e20", "True")]
    [InlineData("xs:float(-9223372036854775808) eq xs:float(-9.223372e18)", "True")]
    [InlineData("-1.0e0 mod xs:double(-9223372036854775808)", "-1")]
    public async Task A_wide_integer_casts_to_double_and_float(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
