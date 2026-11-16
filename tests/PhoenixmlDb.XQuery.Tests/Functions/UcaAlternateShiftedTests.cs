using AwesomeAssertions;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// A UCA collation with <c>alternate=shifted</c> ignores variable characters (spaces,
/// punctuation) at the first three levels and counts them only at the quaternary level (UTS #10
/// §4; QT3 compare-043/044, UCA-params-011 to -015). The parameter was ignored, so a space sorted
/// before every letter: "Akers Styckebruk" came before "Akersberga" under a Swedish collation,
/// and an xsl:merge of a list sorted that way saw it out of order (W3C merge-071).
/// </summary>
public class UcaAlternateShiftedTests
{
    private const string Uca = "http://www.w3.org/2013/collation/UCA?";

    [Theory]
    // The first three levels: the variable characters are ignored.
    [InlineData("database", "data base", "lang=en;alternate=shifted", 0)]
    [InlineData("database", "data base", "lang=en;alternate=shifted;strength=tertiary", 0)]
    [InlineData("a-bc", "abc", "lang=en;alternate=shifted;strength=1", 0)]
    [InlineData("a-bc", "abc", "lang=en;alternate=shifted;strength=3", 0)]
    [InlineData("Akersberga", "Akers Styckebruk", "lang=sv;caseFirst=upper;alternate=shifted", -1)]
    [InlineData("Tanumshede", "Tanums Kommun", "lang=sv;caseFirst=upper;alternate=shifted", -1)]
    // The quaternary level orders what the first three found equal; identical too.
    [InlineData("a-bc", "abc", "lang=en;alternate=shifted;strength=4", -1)]
    [InlineData("a-bc", "abc", "lang=en;alternate=shifted;strength=quaternary", -1)]
    [InlineData("a-bc", "abc", "lang=en;alternate=shifted;strength=5", -1)]
    // Without shifted, the space is a character like any other.
    [InlineData("database", "data base", "lang=en", 1)]
    [InlineData("database", "data base", "lang=en;alternate=non-ignorable", 1)]
    public void Variable_characters_count_only_at_the_quaternary_level(string a, string b, string parameters, int expected)
        => Math.Sign(CollationHelper.CompareUca(a, b, Uca + parameters)).Should().Be(expected);
}
