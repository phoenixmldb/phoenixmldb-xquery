using AwesomeAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// In XQuery a '&amp;' in a string literal must begin a predefined entity or a character
/// reference (XPST0003 otherwise). The lexer refused a bare one with a predicate, which kept every
/// string literal out of its DFA cache (#401: a third of a stylesheet compile's allocation). The
/// lexer adapter checks it now, and reports the position of the '&amp;'.
/// </summary>
public sealed class StringLiteralAmpersandTests
{
    [Theory]
    [InlineData("'AT&T'", 1, 3)]
    [InlineData("\"a &amp; b & c\"", 1, 11)]
    [InlineData("'line one\n  &x'", 2, 2)]
    [InlineData("'&#;'", 1, 1)]
    [InlineData("'&#x;'", 1, 1)]
    [InlineData("'&lt'", 1, 1)]
    [InlineData("'&#X26;'", 1, 1)]
    public void A_bare_ampersand_is_XPST0003_at_its_position(string literal, int line, int column)
    {
        var act = () => new XQueryParserFacade().Parse(literal);
        var error = act.Should().Throw<XQueryParseException>().Which;
        error.ErrorCode.Should().Be("XPST0003");
        error.Errors[0].Line.Should().Be(line);
        error.Errors[0].Column.Should().Be(column);
    }

    [Theory]
    [InlineData("'&lt;&gt;&amp;&quot;&apos;'", "<>&\"'")]
    [InlineData("'&#38;&#x26;'", "&&")]
    public void Entity_and_character_references_are_decoded(string literal, string value)
        => new XQueryParserFacade().Parse(literal).Should().BeOfType<StringLiteral>().Which.Value.Should().Be(value);

    [Fact]
    public void With_raw_ampersands_allowed_a_bare_ampersand_is_a_character()
        => new XQueryParserFacade { AllowRawAmpersand = true }.Parse("'AT&T'").Should().BeOfType<StringLiteral>().Which.Value.Should().Be("AT&T");
}
