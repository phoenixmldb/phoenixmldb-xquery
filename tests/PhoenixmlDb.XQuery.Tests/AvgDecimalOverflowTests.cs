using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:avg sums xs:decimal values in System.Decimal. A sum outside its range is FOAR0002 when the
/// result is an xs:decimal; .NET's OverflowException escaped instead (QT3 cbcl-avg-001). A double
/// in the input makes the result an xs:double, which the decimal sum does not affect.
/// </summary>
public class AvgDecimalOverflowTests
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
    [InlineData("avg((xs:decimal('79000000000000000000000000000'), xs:decimal('79000000000000000000000000000')))")]
    [InlineData("avg((79000000000000000000000000000, 79000000000000000000000000000))")]
    public async Task A_decimal_sum_out_of_range_is_FOAR0002(string query)
    {
        var act = () => Eval(query);
        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        var code = thrown switch
        {
            PhoenixmlDb.XQuery.Functions.XQueryException q => q.ErrorCode,
            XQueryRuntimeException r => r.ErrorCode,
            _ => thrown.GetType().Name,
        };
        code.Should().Be("FOAR0002", thrown.Message);
    }

    [Theory]
    [InlineData("avg((xs:decimal('79000000000000000000000000000'), xs:decimal('79000000000000000000000000000'), 1e0)) gt 5e28", "True")]
    [InlineData("avg((1.5, 2.5)) eq 2", "True")]
    public async Task Other_averages_are_unchanged(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
