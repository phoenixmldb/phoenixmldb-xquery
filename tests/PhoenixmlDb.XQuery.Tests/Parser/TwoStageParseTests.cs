using AwesomeAssertions;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// Parsing is in two stages (#401): SLL prediction, which gives up at the first syntax error,
/// then full LL with error reporting only for input that SLL could not parse. These are parity
/// checks: what a syntax error reports, and the parse of valid input, are those of a full LL parse.
/// </summary>
public sealed class TwoStageParseTests
{
    [Fact]
    public void A_syntax_error_is_reported_by_the_full_parse_with_its_position()
    {
        var act = () => new XQueryParserFacade().Parse("let $x := 1\nreturn $x +");
        var error = act.Should().Throw<XQueryParseException>().Which;
        error.ErrorCode.Should().Be("XPST0003");
        error.Errors.Should().NotBeEmpty();
        error.Errors[0].Line.Should().Be(2);
    }

    [Fact]
    public void Every_syntax_error_is_reported_not_only_the_first()
    {
        var act = () => new XQueryParserFacade().Parse("(1 +, 2 +)");
        act.Should().Throw<XQueryParseException>().Which.Errors.Should().HaveCountGreaterThan(1);
    }

    [Theory]
    [InlineData("for $i in 1 to 3 return $i * 2")]
    [InlineData("<a b='{1}'>{ (1, 2)[. > 1] }</a>")]
    [InlineData("declare function local:f($x) { $x }; local:f(1) instance of xs:integer")]
    public void Valid_input_parses(string query)
        => new XQueryParserFacade().Parse(query).Should().NotBeNull();
}
