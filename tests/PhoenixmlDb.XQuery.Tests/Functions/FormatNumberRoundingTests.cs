using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// format-number rounds half to even (F&amp;O 3.1 §4.7.5). A double is taken as the decimal its
/// shortest round-trip form names, as Saxon takes it. Every expectation here is Saxon-HE 12.10's.
/// </summary>
/// <remarks>
/// It rounded half away from zero, so 2.5 gave 3 and 1234567.765 gave ...,77 (QT3
/// fn-load-xquery-module-040 expects ,76). And a double was cut to 15 significant digits on the way,
/// so 0.1 + 0.2 printed as 0.30000000000000000.
/// </remarks>
public class FormatNumberRoundingTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("format-number(2.5, '0')", "2")]
    [InlineData("format-number(3.5, '0')", "4")]
    [InlineData("format-number(-2.5, '0')", "-2")]
    [InlineData("format-number(0.765, '0.00')", "0.76")]
    [InlineData("format-number(1234567.765, '#,###.##')", "1,234,567.76")]
    // 0.125 is exact in binary, so as a double it is still a tie.
    [InlineData("format-number(0.125e0, '0.00')", "0.12")]
    public async Task ATie_RoundsToEven(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Theory]
    [InlineData("format-number(xs:double('0.765'), '0.00')", "0.76")]
    [InlineData("format-number(2.5e0, '0')", "2")]
    [InlineData("format-number(0.775, '0.00')", "0.78")]
    [InlineData("format-number(1E25, '#####################')", "10000000000000000000000000")]
    [InlineData("format-number(0.1e0 + 0.2e0, '0.00000000000000000')", "0.30000000000000004")]
    public async Task ADouble_RoundsAsItsShortestForm(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
