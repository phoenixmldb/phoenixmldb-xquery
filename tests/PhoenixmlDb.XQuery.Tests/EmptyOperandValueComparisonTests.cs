using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// XPath 3.1 §3.7.1: a value comparison (eq/ne/lt/le/gt/ge) with an
/// empty-sequence operand yields the EMPTY SEQUENCE — not false, not a type
/// error. Surfaced via XSpec (Martin Honnen): an empty xs:integer? variable
/// compared with <c>lt</c> against a number must not raise XPTY0004.
/// </summary>
public class EmptyOperandValueComparisonTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("() lt 65536")]
    [InlineData("() eq 5")]
    [InlineData("() ne 5")]
    [InlineData("() le 5")]
    [InlineData("() gt 5")]
    [InlineData("() ge 5")]
    [InlineData("5 lt ()")]
    [InlineData("() eq ()")]
    public async Task ValueComparison_WithEmptyOperand_IsEmptySequence(string expr)
        => (await _facade.EvaluateAsync($"empty({expr})")).Should().Be("true");

    [Fact]
    public async Task ValueComparison_WithEmptyOperand_HasZeroCount()
        => (await _facade.EvaluateAsync("count(() lt 65536)")).Should().Be("0");

    [Fact]
    public async Task ValueComparison_NonEmptyOperands_StillCompare()
        => (await _facade.EvaluateAsync("3 lt 5")).Should().Be("true");
}
