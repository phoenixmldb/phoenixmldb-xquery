using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Regression tests for arithmetic and aggregate functions applied to derived
/// integer types (xs:long, xs:int, …). These flow through the engine as
/// <c>XsTypedInteger</c>, which previously threw <c>InvalidCastException</c>
/// ("Unable to cast … XsTypedInteger to System.IConvertible") wherever an
/// operator fell through to <c>Convert.ToDouble/ToDecimal</c>.
/// </summary>
public class DerivedIntegerArithmeticTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task UnaryMinus_on_xs_long_is_exact_beyond_double_precision()
    {
        // 17 significant digits — would lose precision if routed through xs:double.
        var result = await _facade.EvaluateAsync("-(xs:long(\"-92233720368547758\"))");
        result.Should().Be("92233720368547758");
    }

    [Fact]
    public async Task UnaryPlus_on_xs_long_preserves_value()
    {
        var result = await _facade.EvaluateAsync("+(xs:long(\"123456789012345\"))");
        result.Should().Be("123456789012345");
    }

    [Fact]
    public async Task RoundHalfToEven_on_xs_int_returns_value()
    {
        var result = await _facade.EvaluateAsync("fn:round-half-to-even(xs:int(\"-2147483648\"))");
        result.Should().Be("-2147483648");
    }

    [Fact]
    public async Task Avg_of_xs_long_values()
    {
        var result = await _facade.EvaluateAsync("avg((xs:long(2), xs:long(4), xs:long(6)))");
        result.Should().Be("4");
    }

    [Fact]
    public async Task Sum_of_xs_long_values()
    {
        var result = await _facade.EvaluateAsync("sum((xs:long(10), xs:long(20), xs:long(30)))");
        result.Should().Be("60");
    }

    [Fact]
    public async Task MaxMin_of_xs_long_values()
    {
        (await _facade.EvaluateAsync("max((xs:long(3), xs:long(9), xs:long(5)))")).Should().Be("9");
        (await _facade.EvaluateAsync("min((xs:long(3), xs:long(9), xs:long(5)))")).Should().Be("3");
    }

    [Fact]
    public async Task Abs_of_xs_int()
    {
        var result = await _facade.EvaluateAsync("abs(xs:int(\"-2147483647\"))");
        result.Should().Be("2147483647");
    }
}
