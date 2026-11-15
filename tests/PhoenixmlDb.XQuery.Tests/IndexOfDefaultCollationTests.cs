using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:index-of with no collation argument compares strings with the default collation
/// (F&amp;O §14.1.2). It compared them by codepoint whatever the default collation was, so
/// under a case-blind default collation index-of(('a','THOU'), 'thou') was empty, where
/// compare() and distinct-values() used the collation (W3C XSLT collations-0117).
/// </summary>
public class IndexOfDefaultCollationTests
{
    private const string CaseBlind = "declare default collation 'http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive'; ";
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData(CaseBlind + "string-join(index-of(('a', 'thou', 'x', 'THOU'), 'THou'), ' ')", "2 4")]
    [InlineData(CaseBlind + "index-of(('a', 'thou', 'x', 'THOU'), 'THou', 'http://www.w3.org/2005/xpath-functions/collation/codepoint')", "")]
    [InlineData(CaseBlind + "index-of((1, 'A', 2.0, xs:untypedAtomic('a')), 2)", "3")]
    [InlineData("index-of(('a', 'thou', 'x', 'THOU'), 'THou')", "")]
    [InlineData("index-of(('a', 'thou', 'x', 'THOU'), 'thou')", "2")]
    [InlineData("declare default collation 'http://www.w3.org/2005/xpath-functions/collation/codepoint'; index-of(('a', 'thou', 'THOU'), 'THOU')", "3")]
    public async Task Index_of_uses_the_default_collation(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
