using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XPath 1.0 backwards-compatible mode (XSLT version="1.0") converts function arguments and
/// arithmetic operands the XPath 1.0 way: a single-item parameter or operand takes the FIRST item,
/// a string parameter gets fn:string() of it and a numeric one fn:number() (XPath 2.0 §3.1.5,
/// §3.4). Each case raised XPTY0004 or returned the empty sequence before.
/// </summary>
public sealed class BackwardsCompatibleConversionTests
{
    private static async Task<List<object?>> RunAsync(string query, bool backwardsCompatible = true)
    {
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine();
        var compiled = engine.Compile(query);
        var ctx = engine.CreateContext();
        ctx.BackwardsCompatible = backwardsCompatible;
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(i);
        return items;
    }

    [Theory]
    [InlineData("round('20.7')", 21.0)]                    // backwards-022: string → number()
    [InlineData("floor(())", double.NaN)]                    // version-021: empty → NaN
    [InlineData("round(())", double.NaN)]                    // xpath-compat-0302
    [InlineData("abs(('-3', '9'))", 3.0)]                    // first item only
    public async Task A_numeric_argument_is_converted_with_number(string query, double expected)
        => (await RunAsync(query)).Should().ContainSingle().Which.Should().Be(expected);

    [Fact]
    public async Task A_double_parameter_accepts_a_string() // xpath-compat-0401
        => (await RunAsync("string-join(subsequence(('a','b','c','d'), '2.00001', '2'), ',')"))
            .Should().Equal("b,c");

    [Fact]
    public async Task A_string_parameter_gets_string_of_the_first_item() // xslt-compat-012
        => (await RunAsync("contains(12.5, '2.5'), contains((1, 'x'), '1')")).Should().Equal(true, true);

    [Fact]
    public async Task An_arithmetic_operand_is_its_first_item() // backwards-024
        => (await RunAsync("1 + (6 to 10)")).Should().ContainSingle().Which.Should().Be(7.0);

    [Fact]
    public async Task Outside_the_mode_the_same_calls_are_still_errors()
    {
        await FluentActions.Awaiting(() => RunAsync("round('20.7')", backwardsCompatible: false))
            .Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>().Where(e => e.ErrorCode == "XPTY0004");
        await FluentActions.Awaiting(() => RunAsync("1 + (6 to 10)", backwardsCompatible: false))
            .Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>().Where(e => e.ErrorCode == "XPTY0004");
    }
}
