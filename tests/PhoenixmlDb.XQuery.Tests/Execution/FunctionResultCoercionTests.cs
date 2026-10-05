using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A function result is coerced to its declared type as an argument is (XPath 3.1 §3.1.5.2): a
/// node result for an atomic type is atomized, and an untyped value is cast, except to a
/// namespace-sensitive type, which is XPTY0117.
/// </summary>
/// <remarks>
/// Two defects. An inline function compared a node result with its atomic return type as is, so
/// <c>function() as xs:integer { &lt;a&gt;5&lt;/a&gt; }</c> failed with XPTY0004. And an untyped result for an
/// xs:QName return type was cast, so <c>as xs:QName { &lt;a&gt;fn:abs&lt;/a&gt; }</c> produced fn:abs
/// (QT3 FunctionCall-019/020/021). #166 had added XPTY0117 for arguments only.
/// </remarks>
public class FunctionResultCoercionTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string> Outcome(string query)
    {
        try { return "value " + await _facade.EvaluateAsync(query); }
        catch (XQueryRuntimeException e) { return e.ErrorCode; }
    }

    [Theory]
    [InlineData("let $f := function() as xs:integer { <a>5</a> } return $f() + 1", "value 6")]
    [InlineData("declare function local:i() as xs:integer { <a>5</a> }; local:i() + 1", "value 6")]
    [InlineData("declare function local:q() as xs:QName { <a>fn:abs</a> }; local:q()", "XPTY0117")]
    [InlineData("let $q := function() as xs:QName { <a>fn:abs</a> } return $q()", "XPTY0117")]
    [InlineData("declare function local:q($l) as xs:QName { <a>fn:{$l}</a> }; local:q(?)('abs')", "XPTY0117")]
    // Control: a typed QName result is returned as it is.
    [InlineData("declare function local:q() as xs:QName { xs:QName('fn:abs') }; local-name-from-QName(local:q())", "value abs")]
    public async Task AFunctionResult_IsCoercedLikeAnArgument(string query, string expected)
        => (await Outcome(query)).Should().Be(expected);
}
