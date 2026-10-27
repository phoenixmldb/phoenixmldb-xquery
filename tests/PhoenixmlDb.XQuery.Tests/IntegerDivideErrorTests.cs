using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// idiv by zero is FOAR0001 whatever the dividend. An infinite dividend is FOAR0002, and was
/// checked first, so INF idiv 0 gave FOAR0002 (QT3 op-numeric-integer-dividefloat2args-4,
/// -dividedouble2args-4).
/// </summary>
public class IntegerDivideErrorTests
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
    [InlineData("xs:float('INF') idiv xs:float('0')", "FOAR0001")]
    [InlineData("xs:double('-INF') idiv xs:double('-0')", "FOAR0001")]
    [InlineData("xs:double('INF') idiv 0", "FOAR0001")]
    [InlineData("xs:double('INF') idiv 0.0", "FOAR0001")]
    [InlineData("xs:double('INF') idiv 2", "FOAR0002")]
    [InlineData("xs:double('NaN') idiv 1", "FOAR0002")]
    [InlineData("1 idiv xs:float(0)", "FOAR0001")]
    public async Task The_error_code_follows_the_operands(string query, string expected)
        => (await ErrorCode(query)).Should().Be(expected);
}
