using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:round-half-to-even on xs:double decides ties from the value's ACTUAL binary form, not
/// from its shortest decimal spelling.
///
/// The implementation used Math.Round(val, precision, MidpointRounding.ToEven), whose double
/// overload applies a decimal-style correction and so manufactures ties that binary does not
/// have. Found by W3C test math-3303 once the XSLT runner stopped passing &lt;assert&gt;
/// unconditionally — the corpus had corrected these expectations in 2024
/// (w3c/xslt30-test issue 79) and we could not see it.
/// </summary>
public class RoundHalfToEvenBinaryTests
{
    private readonly XQueryFacade _facade = new();

    /// <summary>
    /// Asserts on the VALUE, not its serialized form. The facade renders a double in
    /// scientific notation (250.03 comes back as "2.5003e2"), so comparing strings here would
    /// pin the serializer instead of the rounding — and would fail even when the arithmetic is
    /// exactly right, which is how the first version of these tests behaved.
    /// </summary>
    private async Task<string> Eq(string expr, string expected)
        => await _facade.EvaluateAsync($"({expr}) eq ({expected})");

    private async Task<string> Str(string expr)
        => await _facade.EvaluateAsync($"string({expr})");

    /// <summary>
    /// The nearest double to 250.025 is 250.025000000000005684341886080801486968994140625 —
    /// strictly ABOVE the midpoint, so this is not a tie and rounds UP.
    /// </summary>
    [Fact]
    public async Task Above_midpoint_rounds_up_even_though_it_looks_like_a_tie()
        => (await Eq("round-half-to-even(250.0250e0, 2)", "250.03e0")).Should().Be("true");

    /// <summary>
    /// The controls, and the reason the old code looked correct. 150.015 as a double is BELOW
    /// its midpoint and 180.018 ABOVE, so decimal-thinking and binary happen to agree on both.
    /// Only 250.025 separates them — which is exactly why it was the case W3C had to correct.
    /// A fix that erred the other way would break these two.
    /// </summary>
    [Theory]
    [InlineData("150.0150e0", "150.01")]
    [InlineData("180.0180e0", "180.02")]
    [InlineData("-150.0150e0", "-150.01")]
    [InlineData("-250.0250e0", "-250.03")]
    [InlineData("-120.0120e0", "-120.01")]
    public async Task Sibling_cases_are_unchanged(string input, string expected)
        => (await Eq($"round-half-to-even({input}, 2)", expected + "e0")).Should().Be("true");

    /// <summary>A value that IS exactly a midpoint still goes to even.</summary>
    [Theory]
    [InlineData("0.5", "0")]
    [InlineData("1.5", "2")]
    [InlineData("2.5", "2")]
    [InlineData("3.5", "4")]
    [InlineData("-2.5", "-2")]
    public async Task Genuine_ties_still_go_to_even(string input, string expected)
        => (await Eq($"round-half-to-even({input})", expected)).Should().Be("true");

    [Theory]
    [InlineData("150.0e0", -2, "200")]
    [InlineData("250.0e0", -2, "200")]
    [InlineData("120.0e0", -2, "100")]
    [InlineData("-250.0e0", -2, "-200")]
    public async Task Negative_precision(string input, int precision, string expected)
        => (await Eq($"round-half-to-even({input}, {precision})", expected + "e0")).Should().Be("true");

    /// <summary>
    /// A precision at which the value is already exact must return it UNCHANGED. This caught a
    /// regression in the first version of the fix: BigInteger arithmetic is exact, but
    /// converting back divides by 10^precision, and 10^100 is not a representable double — so
    /// the double rounding landed on a neighbour and a no-op changed the value to
    /// 1.2345000000000002. The remainder being zero means "already exact", so the answer is
    /// the input and no conversion happens at all.
    /// </summary>
    [Theory]
    [InlineData("1.2345e0", 100, "1.2345")]
    [InlineData("1.5e0", 400, "1.5")]
    [InlineData("2.5e0", 30, "2.5")]
    public async Task Precision_beyond_the_value_is_a_true_no_op(string input, int precision, string expected)
        // "eq the input" is the whole point: a no-op must return the SAME double, not a
        // neighbour. The first version of the fix returned 1.2345000000000002 here.
        => (await Eq($"round-half-to-even({input}, {precision})", expected + "e0")).Should().Be("true");

    [Theory]
    [InlineData("xs:double('NaN')", "NaN")]
    [InlineData("xs:double('INF')", "INF")]
    public async Task Specials_pass_through(string input, string expected)
        => (await Str($"round-half-to-even({input})")).Should().Be(expected);

    /// <summary>
    /// Negative zero must survive. `eq` cannot see it — -0 eq 0 is true — so probe the sign
    /// through division, which is the only way the difference is observable.
    /// </summary>
    [Fact]
    public async Task Negative_zero_keeps_its_sign()
        => (await Eq("1 div round-half-to-even(-3.0e0, -2)", "-xs:double('INF')")).Should().Be("true");

    /// <summary>Only the double path changed; decimal and integer keep their own rounding.</summary>
    [Theory]
    [InlineData("xs:decimal('2.5')", "2")]
    [InlineData("xs:float('2.5')", "2")]
    [InlineData("35612, -2", "35600")]
    public async Task Other_numeric_types_are_unaffected(string input, string expected)
        => (await Eq($"round-half-to-even({input})", expected)).Should().Be("true");
}
