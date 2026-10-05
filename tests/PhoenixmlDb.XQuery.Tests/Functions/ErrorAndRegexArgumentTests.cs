using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// fn:analyze-string reports an invalid pattern as FORX0002, and fn:error's first argument must
/// be an xs:QName.
/// </summary>
/// <remarks>
/// analyze-string did not catch the regex constructor's exception: an invalid pattern escaped as a
/// raw .NET ArgumentException, with no XQuery code, where matches, replace and tokenize report
/// FORX0002 (QT3 analyzeString-901). And fn:error turned any first argument into a code with
/// ToString(), so fn:error('text') raised an error named "text" instead of XPTY0004 (fn-error-3).
/// </remarks>
public class ErrorAndRegexArgumentTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string> CodeOf(string query)
    {
        try { return "value " + await _facade.EvaluateAsync(query); }
        catch (PhoenixmlDb.XQuery.Execution.XQueryRuntimeException e) { return e.ErrorCode; }
        catch (PhoenixmlDb.XQuery.Functions.XQueryException e) { return e.ErrorCode; }
    }

    [Theory]
    [InlineData("analyze-string('abc', ')-(')", "FORX0002")]
    [InlineData("analyze-string('abc', '[')", "FORX0002")]
    [InlineData("analyze-string('abc', 'a{2,1}')", "FORX0002")]
    [InlineData("fn:error('Wrong Argument Type')", "XPTY0004")]
    [InlineData("fn:error(42, 'description')", "XPTY0004")]
    // Controls: a QName code is raised as itself, the empty code is FOER0000, a valid pattern works.
    [InlineData("fn:error(QName('urn:e', 'e:BAD1'), 'd')", "BAD1")]
    [InlineData("fn:error((), 'd')", "FOER0000")]
    [InlineData("count(analyze-string('abc', 'b')/*)", "value 3")]
    public async Task Codes(string query, string expected)
        => (await CodeOf(query)).Should().Be(expected);
}
