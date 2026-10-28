using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The prefix of a declared variable's name must be bound (XPST0081). It was checked only where
/// the variable is referenced, so an unused declaration passed: after declare namespace p = ""
/// removed p, declare variable $p:v external was accepted (QT3 K2-ExternalVariablesWithout-3).
/// </summary>
public class DeclaredVariablePrefixTests
{
    private static IReadOnlyList<string> CompileErrorCodes(string query)
    {
        try
        {
            return new QueryEngine().Compile(query).Errors.Select(e => e.Code).ToList();
        }
        catch (PhoenixmlDb.XQuery.Parser.XQueryParseException ex)
        {
            return [ex.Message];
        }
    }

    [Theory]
    [InlineData("declare namespace p = ''; declare variable $p:v external; 1")]
    [InlineData("declare namespace p = ''; declare variable $p:v := 2; 1")]
    [InlineData("declare default element namespace ''; declare namespace prefix = ''; declare variable $prefix:input-context external; 1")]
    public void An_unbound_prefix_in_a_declared_name_is_XPST0081(string query)
        => CompileErrorCodes(query).Should().Contain(c => c.Contains("XPST0081"));

    [Theory]
    [InlineData("declare namespace p = 'urn:p'; declare variable $p:v := 2; 1")]
    [InlineData("declare variable $local:v := 2; 1")]
    [InlineData("declare variable $Q{urn:q}v := 2; 1")]
    [InlineData("declare variable $v := 2; 1")]
    public void A_bound_prefix_or_no_prefix_is_fine(string query)
        => CompileErrorCodes(query).Should().BeEmpty();
}
