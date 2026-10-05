using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// A direct comment or processing instruction is XML syntax: one that XML does not allow does not
/// parse (XPST0003). And fn:parse-ietf-date reports an impossible date or time as FORG0010.
/// </summary>
/// <remarks>
/// The direct forms reached the computed constructors' run-time checks and raised their codes,
/// XQDY0072 and XQDY0064 (QT3 Constr-comment-*, Constr-pi-target-*, K2-DirectConOther-*). An
/// impossible date (day 32, 29 February 2014, hour 29, offset -15:00) escaped as a .NET exception
/// with no XQuery code (parse-ietf-date-errs*).
/// </remarks>
public class DirectConstructorSyntaxTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string> Outcome(string query)
    {
        try { return "value " + await _facade.EvaluateAsync(query); }
        catch (PhoenixmlDb.XQuery.Parser.XQueryParseException e) { return e.Message[..8]; }
        catch (PhoenixmlDb.XQuery.Execution.XQueryRuntimeException e) { return e.ErrorCode; }
    }

    [Theory]
    [InlineData("<!--a--b-->", "XPST0003")]
    [InlineData("<!--a--->", "XPST0003")]
    [InlineData("<?xml ?>", "XPST0003")]
    [InlineData("<?XmL?>", "XPST0003")]
    [InlineData("parse-ietf-date('Mon, 32 Aug 2014 19:36:01 GMT')", "FORG0010")]
    [InlineData("parse-ietf-date('Sat, 29 Feb 2014 19:36:01 GMT')", "FORG0010")]
    [InlineData("parse-ietf-date('Aug 20 29:36:01GMT 2014')", "FORG0010")]
    // Controls: valid direct forms, a computed comment's own check, and a valid date.
    [InlineData("string(<!--a - b-->)", "value a - b")]
    [InlineData("name(<?xml-stylesheet href='a'?>)", "value xml-stylesheet")]
    [InlineData("comment { 'a--b' }", "XQDY0072")]
    [InlineData("string(parse-ietf-date('Wed, 06 Jun 1994 07:29:35 GMT'))", "value 1994-06-06T07:29:35Z")]
    public async Task DirectConstructorAndIetfDateErrors(string query, string expected)
        => (await Outcome(query)).Should().Be(expected);
}
