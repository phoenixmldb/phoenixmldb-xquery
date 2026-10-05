using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// The error a failing cast raises, per F&amp;O 3.1 §19 and XQuery 3.1 §3.18.2. Each had the wrong
/// code, the same through a constructor function (xs:decimal(...)) and `cast as`.
/// </summary>
public class CastErrorCodeTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string> CodeOf(string query)
    {
        try { return "value " + await _facade.EvaluateAsync(query); }
        catch (PhoenixmlDb.XQuery.Execution.XQueryRuntimeException e) { return e.ErrorCode + " " + e.Message; }
        catch (PhoenixmlDb.XQuery.Parser.XQueryParseException e) { return e.Message; }
    }

    [Theory]
    // NaN and ±INF have no decimal or integer value (was FORG0001 / FOCA0003).
    [InlineData("xs:decimal(xs:float('NaN'))", "FOCA0002")]
    [InlineData("xs:decimal(xs:double('INF'))", "FOCA0002")]
    [InlineData("xs:integer(xs:double('-INF'))", "FOCA0002")]
    [InlineData("xs:float('NaN') cast as xs:integer", "FOCA0002")]
    // A finite value beyond the decimals held is FOCA0001 (was FORG0001 / FOCA0002).
    [InlineData("xs:decimal(99e100)", "FOCA0001")]
    [InlineData("1.7976931348623157E+308 cast as xs:decimal", "FOCA0001")]
    [InlineData("xs:decimal(123456789012345678901234567890123)", "FOCA0001")]
    // An integer outside a bounded subtype is FORG0001 (was XPTY0004).
    [InlineData("xs:long(9223372036854775808)", "FORG0001")]
    public async Task ARuntimeCastFailure_HasTheSpecifiedCode(string query, string code)
        => (await CodeOf(query)).Should().Contain(code);

    [Theory]
    // xs:integer is unbounded: a large finite float or double is an exact integer, not an error.
    [InlineData("string-length(string(xs:float('3.402823e38') cast as xs:integer))", "39")]
    [InlineData("xs:integer(1e30)", "1000000000000000019884624838656")]
    [InlineData("xs:integer(-2.9e0)", "-2")]
    public async Task ALargeFiniteDouble_CastsToAnExactInteger(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Theory]
    // As a cast target: xs:anySimpleType is XPST0080; a type that is not atomic, or not defined,
    // is XQST0052 (both were XPST0051).
    [InlineData("'s' cast as xs:anySimpleType", "XPST0080")]
    [InlineData("'s' castable as xs:anySimpleType", "XPST0080")]
    [InlineData("'s' cast as xs:untyped", "XQST0052")]
    [InlineData("'s' cast as xs:anyType", "XQST0052")]
    [InlineData("3 cast as xs:doesNotExist", "XQST0052")]
    // Not a cast target: a SequenceType keeps XPST0051.
    [InlineData("'s' instance of xs:anySimpleType", "XPST0051")]
    [InlineData("3 instance of xs:doesNotExist", "XPST0051")]
    public async Task AnInvalidTargetType_HasTheSpecifiedCode(string query, string code)
        => (await CodeOf(query)).Should().Contain(code);
}
