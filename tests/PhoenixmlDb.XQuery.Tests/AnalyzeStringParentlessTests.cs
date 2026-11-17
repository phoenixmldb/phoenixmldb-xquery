using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:analyze-string returns its fn:analyze-string-result element with no parent (Saxon-HE
/// 12.10: empty(analyze-string("ab", "b")/..) is true). It had the document it was parsed in as
/// parent, so ../ and root() reached a document node.
/// </summary>
public sealed class AnalyzeStringParentlessTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("""empty(analyze-string("ab", "b")/..)""", "true")]
    [InlineData("""root(analyze-string("ab", "b")) instance of element()""", "true")]
    [InlineData("""count(analyze-string("a#b", "#")/*)""", "3")]
    [InlineData("""analyze-string("ab", "(a)(b)")//*:group[@nr = 2]/string()""", "b")]
    public async Task The_result_element_has_no_parent(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
