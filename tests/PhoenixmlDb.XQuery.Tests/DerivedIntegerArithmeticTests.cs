using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Regression tests for numeric operators/aggregates applied to derived integer
/// types (xs:long, xs:int, …), which flow through the engine as <c>XsTypedInteger</c>.
/// </summary>
/// <remarks>
/// These cases are self-contained in PhoenixmlDb.XQuery: the operators unwrap
/// <c>XsTypedInteger</c> to its CLR <c>long</c> before computing. The companion
/// cases that route through <c>Convert.ToDouble/ToDecimal</c> — fn:avg,
/// fn:round-half-to-even, fn:abs — depend on PhoenixmlDb.Core making
/// <c>XsTypedInteger</c> implement <see cref="System.IConvertible"/>; they are
/// verified in the Core test suite (XdmValueTests) and pass here once the Core
/// package pin is bumped to that build.
/// </remarks>
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
}
