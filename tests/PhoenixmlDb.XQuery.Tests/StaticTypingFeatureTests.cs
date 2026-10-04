using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The Static Typing Feature (CompilationOptions.StrictTypeChecking): expressions whose static
/// type is known not to fit are static errors, even when the value at run time would fit; it is
/// off by default; and it never rejects a query merely because a type cannot be inferred.
/// </summary>
public class StaticTypingFeatureTests
{
    private const string AlwaysTrue = "fn:current-dateTime() eq fn:dateTime(fn:current-date(), fn:current-time())";

    private static QueryCompilationResult Compile(string query, bool strict)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        return engine.Compile(query, new CompilationOptions { StrictTypeChecking = strict });
    }

    [Theory]
    [InlineData("fn:function-arity(if (" + AlwaysTrue + ") then fn:dateTime#2 else 1)", "XPTY0004")]       // integer is no function
    [InlineData("fn:function-arity(if (" + AlwaysTrue + ") then fn:dateTime#2 else ())", "XPTY0004")]      // may be empty
    [InlineData("fn:unparsed-text(if (" + AlwaysTrue + ") then 'a.txt' else 1)", "XPTY0004")]              // integer is no string
    [InlineData("fn:filter(1 to 10, function($a) { if ($a eq 100) then 0 else fn:true() })", "XPTY0004")] // result may be integer
    [InlineData("(if (" + AlwaysTrue + ") then . else 1) ! fn:has-children()", "XPTY0004")]                // focus may be atomic
    [InlineData("for $x in (1, 2) where ($x, 1) return $x", "XPTY0004")]                                  // no EBV for (item, item)
    [InlineData("fn:count(/@*)", "XPST0005")]                                                              // a document has no attributes
    [InlineData("fn:count(//center/self::nowhere)", "XPST0005")]                                           // element(center) is never nowhere
    [InlineData("fn:count(//center/@a/self::*)", "XPST0005")]                                              // an attribute is no element
    [InlineData("fn:count(/..)", "XPST0005")]                                                              // a document has no parent
    public void KnownMismatch_IsAStaticError(string query, string code)
    {
        var result = Compile(query, strict: true);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == code);
    }

    [Theory]
    [InlineData("fn:function-arity(if (" + AlwaysTrue + ") then fn:dateTime#2 else 1)")]
    [InlineData("fn:count(/@*)")]
    public void WithoutTheFeature_TheSameQueriesCompile(string query)
        => Compile(query, strict: false).Success.Should().BeTrue();

    [Theory]
    [InlineData("fn:function-arity(fn:dateTime#2)")]
    [InlineData("fn:filter(1 to 10, function($a) { $a mod 2 = 0 })")]
    [InlineData("fn:count(//center/child::*)")]
    [InlineData("fn:count(//center/@a)")]
    [InlineData("declare variable $v external; fn:function-arity($v)")]          // $v unknown: no claim
    [InlineData("declare variable $v external; fn:string-length($v)")]
    [InlineData("for $x in (1, 2) where $x eq 1 return $x")]
    [InlineData("fn:abs(if (" + AlwaysTrue + ") then 1 else 2.5)")]              // integer | decimal both numeric
    [InlineData("fn:string-length(if (" + AlwaysTrue + ") then 'a' else xs:anyURI('b'))")] // anyURI promotes
    public void FittingOrUnknownTypes_Compile(string query)
        => Compile(query, strict: true).Success.Should().BeTrue();
}
