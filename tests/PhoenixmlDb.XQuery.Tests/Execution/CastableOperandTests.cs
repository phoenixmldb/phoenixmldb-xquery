using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// `castable as` atomizes its operand first (XQuery 3.1 §3.18.3), and a target written with "?"
/// accepts the empty sequence even when it is a list type.
/// </summary>
/// <remarks>
/// A map or function operand returned false: atomization never ran, and the cast's catch-all
/// swallowed the failure. It is FOTY0013. And "?" on a list target was overwritten, because a list
/// target widens its occurrence to "*", so <c>() castable as xs:NMTOKENS?</c> was false.
/// </remarks>
public class CastableOperandTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("map{} castable as xs:integer")]
    [InlineData("[[], (), [[3, map{}]]] castable as xs:integer")]
    [InlineData("fn:abs#1 castable as xs:integer")]
    public async Task AnOperandWithNoTypedValue_IsFOTY0013(string query)
    {
        var act = () => _facade.EvaluateAsync(query);
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("Atomization is not defined");
    }

    [Theory]
    [InlineData("() castable as xs:NMTOKENS?", "true")]
    [InlineData("() castable as xs:IDREFS?", "true")]
    [InlineData("count(() cast as xs:ENTITIES?)", "0")]
    // Controls: these were already right.
    [InlineData("() castable as xs:NMTOKENS", "false")]
    [InlineData("[1] castable as xs:integer", "true")]
    [InlineData("[1, 2] castable as xs:integer", "false")]
    [InlineData("<a>5</a> castable as xs:integer", "true")]
    public async Task Castable_Answers(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
