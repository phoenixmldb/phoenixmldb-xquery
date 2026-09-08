using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The adaptive output method has its own rule for xs:double, and it is NOT fn:string.
/// W3C XSLT and XQuery Serialization 3.1 §10:
/// <para>
/// <i>"An instance of xs:double is serialized by applying the function
/// format-number(?, '0.0##########################e0')"</i> — with exponent-separator <c>e</c>,
/// infinity <c>INF</c>, NaN <c>NaN</c>.
/// </para>
/// <para>
/// Pinned because two implementations of this existed and disagreed: the library returned
/// <c>4.2e1</c> for <c>xs:double(41) + 1</c> while the <c>xquery</c> CLI's own serializer printed
/// <c>42</c> under <c>-o adaptive</c>. Anything embedding the library got one answer, CLI users
/// the other. The CLI now delegates here, so there is one implementation — these tests guard it.
/// </para>
/// </summary>
public class AdaptiveDoubleSerializationTests
{
    [Theory]
    [InlineData(42.0, "4.2e1")]
    [InlineData(0.5, "5.0e-1")]
    [InlineData(1.0, "1.0e0")]
    [InlineData(-2.5, "-2.5e0")]
    [InlineData(1234567.0, "1.234567e6")]
    [InlineData(0.0, "0.0e0")]
    public void AdaptiveDouble_UsesTheExponentialFormTheSpecRequires(double value, string expected)
        => XQueryResultSerializer.FormatAdaptiveDouble(value).Should().Be(expected);

    /// <summary>INF/-INF/NaN keep their lexical forms rather than being formatted.</summary>
    [Theory]
    [InlineData(double.PositiveInfinity, "INF")]
    [InlineData(double.NegativeInfinity, "-INF")]
    [InlineData(double.NaN, "NaN")]
    public void AdaptiveDouble_KeepsSpecialLexicalForms(double value, string expected)
        => XQueryResultSerializer.FormatAdaptiveDouble(value).Should().Be(expected);

    /// <summary>
    /// Negative zero is distinct from zero and must keep its sign — the spec's format-number
    /// picture carries the minus through.
    /// </summary>
    [Fact]
    public void AdaptiveDouble_KeepsNegativeZero()
        => XQueryResultSerializer.FormatAdaptiveDouble(-0.0).Should().Be("-0.0e0");
}
