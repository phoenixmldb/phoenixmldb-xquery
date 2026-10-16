using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Arithmetic is defined on numbers, durations and dates and times; any other operand is
/// XPTY0004. An xs:boolean was taken as 1 or 0 (1 + true() was 2), and xs:anyURI, xs:hexBinary
/// and xs:base64Binary escaped as a FormatException or an InvalidCastException.
/// </summary>
public sealed class ArithmeticOperandTypeTests
{
    [Theory]
    [InlineData("1 + true()")]
    [InlineData("false() * 2")]
    [InlineData("true() - true()")]
    [InlineData("1 + xs:anyURI('a')")]
    [InlineData("1 + xs:hexBinary('00')")]
    [InlineData("xs:base64Binary('AA==') div 2")]
    public async Task A_non_arithmetic_operand_is_XPTY0004(string query)
    {
        var act = () => new XQueryFacade().EvaluateAsync(query);
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }

    [Theory]
    [InlineData("1 + 1", "2")]
    [InlineData("xs:untypedAtomic('1') + 1", "2")]
    [InlineData("xs:dayTimeDuration('PT1H') * 2", "PT2H")]
    [InlineData("number(true()) + 1", "2")]
    public async Task Arithmetic_on_its_operands_is_unchanged(string query, string expected)
        => (await new XQueryFacade().EvaluateAsync($"string({query})")).Should().Be(expected);
}
