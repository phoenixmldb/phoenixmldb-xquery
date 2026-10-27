using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The function conversion rules cast an xs:untypedAtomic argument or result to the required
/// atomic type (XPath 3.1 §3.1.5.2). A value with no valid form in that type raises the cast's
/// error, FORG0001; the call sites kept the uncast value and reported XPTY0004, or for a
/// sequence type bound it uncast (QT3 K2-FunctionProlog-24).
/// </summary>
public class UntypedConversionCastErrorTests
{
    private static async Task<string> Eval(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("declare function local:f($b as xs:boolean) { $b }; local:f(<a/>)")]
    [InlineData("function($b as xs:boolean) { $b }(<a/>)")]
    [InlineData("declare function local:f($b as xs:integer*) { $b }; local:f((<a>1</a>, <a>x</a>))")]
    [InlineData("declare function local:f($d as xs:date) { $d }; local:f(<a>2012-13-45</a>)")]
    [InlineData("declare function local:f() as xs:boolean { <a/> }; local:f()")]
    [InlineData("declare function local:f($n as node()*) as xs:boolean* { for $a in $n return $a }; local:f((<e1/>, <e2/>))")]
    [InlineData("function() as xs:boolean { <a/> }()")]
    public async Task An_untyped_value_that_does_not_cast_is_FORG0001(string query)
    {
        var act = () => Eval(query);
        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        var code = thrown switch
        {
            PhoenixmlDb.XQuery.Functions.XQueryException q => q.ErrorCode,
            XQueryRuntimeException r => r.ErrorCode,
            _ => thrown.GetType().Name,
        };
        code.Should().Be("FORG0001", thrown.Message);
    }

    [Theory]
    [InlineData("declare function local:f($b as xs:integer) { $b + 1 }; local:f(<a> 7 </a>)", "8")]
    [InlineData("declare function local:f($b as xs:integer*) { sum($b) }; local:f((<a>1</a>, <a>2</a>))", "3")]
    [InlineData("declare function local:f() as xs:boolean { <a>true</a> }; local:f()", "True")]
    public async Task An_untyped_value_that_casts_is_converted(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
