using AwesomeAssertions;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// validate { … } and extension expressions are ValueExprs (XQuery 3.1 §A.1): an operand of an
/// operator, but not the start of a postfix, path or simple map expression without parentheses.
/// validate { … }/* was accepted (QT3 qischema90007 expects XPST0003).
/// </summary>
public class ValidateValueExprTests
{
    private static string? ParseError(string query)
    {
        try
        {
            new XQueryParserFacade().Parse(query);
            return null;
        }
        catch (XQueryParseException ex)
        {
            return ex.Message;
        }
    }

    [Theory]
    [InlineData("validate { <a/> }/*")]
    [InlineData("validate { <a/> }//b")]
    [InlineData("validate lax { <a/> }[1]")]
    [InlineData("validate { <a/> } ! name()")]
    [InlineData("validate { <a/> }?x")]
    [InlineData("(# local:p #) { <a/> }/*")]
    [InlineData("<x/> ! validate { . }")]
    public void Followed_without_parentheses_is_XPST0003(string query)
        => ParseError(query).Should().Contain("XPST0003");

    [Theory]
    [InlineData("validate { <a/> }")]
    [InlineData("(validate { <a/> })/*")]
    [InlineData("count(validate { <a/> })")]
    [InlineData("validate { <a/> } is validate { <b/> }")]
    [InlineData("-validate { 1 }")]
    [InlineData("(# local:p #) { 1 } + 1")]
    [InlineData("a/validate")]
    public void Standing_alone_parses(string query)
        => ParseError(query).Should().BeNull();
}
