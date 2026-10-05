using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Error codes for three misjudged cases (QT3 misc-CombinedErrorCodes and kin).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A non-node in the left operand of `/` is XPTY0019 (XPath 3.1 §3.3.1.1); XPTY0020 is only
/// for a bare axis step whose context item is not a node. Every case was XPTY0020, and a mixed
/// intermediate step raised the final-result mixture error, XPTY0018.</item>
/// <item>A computed element or attribute name of a type other than xs:QName, xs:string or
/// xs:untypedAtomic is XPTY0004 (§3.9.3.1). Every value was parsed as a name, giving XQDY0074.</item>
/// <item>A validate operand that is not one document or element node is XQTY0030 (§3.21). It
/// raised XQDY0025, the code for a duplicate attribute name.</item>
/// </list>
/// </remarks>
public class PathAndConstructorErrorCodeTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string> Outcome(string query)
    {
        try { return "value " + await _facade.EvaluateAsync(query); }
        catch (XQueryRuntimeException e) { return e.ErrorCode; }
        catch (PhoenixmlDb.XQuery.Functions.XQueryException e) { return e.ErrorCode; }
    }

    [Theory]
    [InlineData("<a/>/1/node()", "XPTY0019")]
    [InlineData("let $x := 1 return $x/a", "XPTY0019")]
    [InlineData("let $x := <a><b/><b/></a> return $x/b/(if (position() eq 1) then 1 else <a/>)/a", "XPTY0019")]
    // Controls: a bare step on a non-node context, and a mixed FINAL result.
    [InlineData("1 ! node()", "XPTY0020")]
    [InlineData("<a><b/></a>/(b, 1)", "XPTY0018")]
    [InlineData("count(<a><b/></a>/b/(.)/self::b)", "value 1")]
    public async Task PathErrors(string query, string expected)
        => (await Outcome(query)).Should().Be(expected);

    [Theory]
    [InlineData("element { 1 } { }", "XPTY0004")]
    [InlineData("attribute { 1 } { 1 }", "XPTY0004")]
    [InlineData("element { xs:date('2007-11-28') } { }", "XPTY0004")]
    // Controls: a string that is not a QName is still XQDY0074; string, untyped and QName names work.
    [InlineData("element { 'a b' } { }", "XQDY0074")]
    [InlineData("name(element { 'x' } { })", "value x")]
    [InlineData("name(element { xs:untypedAtomic('u') } { })", "value u")]
    [InlineData("name(attribute { xs:token('t') } { 1 })", "value t")]
    public async Task ComputedNameErrors(string query, string expected)
        => (await Outcome(query)).Should().Be(expected);

    [Theory]
    [InlineData("validate { 1 }")]
    [InlineData("validate strict { () }")]
    public async Task ValidateOfANonNode_IsXQTY0030(string query)
        => (await Outcome(query)).Should().Be("XQTY0030");
}
