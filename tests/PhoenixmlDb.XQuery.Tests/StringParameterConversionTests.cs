using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The function conversion rules (XPath 3.1 §3.1.5.2) give an xs:string parameter a string, an
/// xs:untypedAtomic cast to xs:string, or an xs:anyURI promoted to it. A number or a boolean is
/// XPTY0004. Built-ins took its string form: unparsed-text(1) looked for a resource named "1"
/// (QT3 fn-unparsed-text-008, fn-unparsed-text-lines-008).
/// </summary>
public class StringParameterConversionTests
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
    [InlineData("unparsed-text(1)")]
    [InlineData("unparsed-text-lines(1)")]
    [InlineData("upper-case(12)")]
    [InlineData("string-length(true())")]
    [InlineData("normalize-unicode('a', 1)")]
    [InlineData("declare function local:f($s as xs:string) { $s }; local:f(1)")]
    public async Task A_number_or_boolean_for_a_string_parameter_is_XPTY0004(string query)
    {
        var act = () => Eval(query);
        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        var code = thrown switch
        {
            PhoenixmlDb.XQuery.Functions.XQueryException q => q.ErrorCode,
            XQueryRuntimeException r => r.ErrorCode,
            _ => thrown.GetType().Name,
        };
        code.Should().Be("XPTY0004");
    }

    [Theory]
    [InlineData("upper-case(<a>x</a>)", "X")]
    [InlineData("upper-case(xs:untypedAtomic('x'))", "X")]
    [InlineData("upper-case(xs:anyURI('x'))", "X")]
    [InlineData("upper-case(xs:NCName('x'))", "X")]
    [InlineData("string-length(())", "0")]
    [InlineData("concat(1, true())", "1true")]
    [InlineData("string-join((1, 2), '-')", "1-2")]
    public async Task A_string_untyped_value_or_URI_is_accepted(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
