using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// CompilationOptions.XPath compiles the text as XPath 3.1. Each case is one where the two
/// languages give different answers for the same text, so every assertion is paired with the
/// XQuery result (QT3 Literals056a, PathExpr-5p, typeswitch-in-xpath, LetExpr020a, inline-fn-016,
/// serialize-xml-012, line-ending-P002).
/// </summary>
public class XPathModeTests
{
    private static async Task<string> Eval(string text, bool xpath)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(text, new CompilationOptions { XPath = xpath });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    private static string? StaticError(string text, bool xpath)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        try
        {
            var compiled = engine.Compile(text, new CompilationOptions { XPath = xpath });
            return compiled.Success ? null : string.Join("; ", compiled.Errors);
        }
        catch (Exception ex) when (ex is XQueryParseException or PhoenixmlDb.Core.XQueryException)
        {
            return ex.Message;
        }
    }

    [Theory]
    [InlineData("\"&amp;\"", "&amp;", "&")]
    [InlineData("'a &lt; b'", "a &lt; b", "a < b")]
    [InlineData("string(\"t\" eq \"&#x74;\")", "false", "true")]
    [InlineData("string-length('a\r\nb')", "4", "3")]
    public async Task Literal_text_is_verbatim_in_XPath(string text, string asXPath, string asXQuery)
    {
        (await Eval(text, xpath: true)).Should().Be(asXPath);
        (await Eval(text, xpath: false)).Should().Be(asXQuery);
    }

    [Theory]
    [InlineData("count(.[/ < 5])")]
    public void A_less_than_is_a_comparison_in_XPath(string text)
    {
        StaticError(text, xpath: true).Should().BeNull();
        StaticError(text, xpath: false).Should().Contain("XPST0003");
    }

    [Theory]
    [InlineData("typeswitch (10) case $i as xs:integer return 1 default $v return 0")]
    [InlineData("switch (1) case 1 return 'a' default return 'b'")]
    [InlineData("try { 1 } catch * { 2 }")]
    [InlineData("let $a := 1 let $b := $a return $b")]
    [InlineData("for $a in 1 where $a return $a")]
    [InlineData("for $a at $p in 1 return $p")]
    [InlineData("let $a as xs:integer := 1 return $a")]
    [InlineData("some $a as xs:integer in 1 satisfies $a")]
    [InlineData("%Q{http://example.com/a}x function() { 1 }")]
    [InlineData("<a/>")]
    [InlineData("element a { 1 }")]
    [InlineData("ordered { 1 }")]
    [InlineData("``[x]``")]
    [InlineData("declare variable $x := 1; $x")]
    [InlineData("xquery version '3.1'; 1")]
    public void XQuery_only_syntax_is_XPST0003_in_XPath(string text)
    {
        StaticError(text, xpath: true).Should().Contain("XPST0003");
        StaticError(text, xpath: false).Should().BeNull();
    }

    [Theory]
    [InlineData("for $a in (1, 2), $b in 3 return $a + $b", "4,5")]
    [InlineData("let $a := 1, $b := $a + 1 return $b", "2")]
    [InlineData("string(every $a in (1, 2) satisfies $a > 0)", "true")]
    [InlineData("function($a as xs:integer) as xs:integer { $a + 1 }(1)", "2")]
    [InlineData("map { 'k': [1, 2] }?k?2", "2")]
    public async Task XPath_syntax_still_parses(string text, string expected)
        => (await Eval(text, xpath: true)).Should().Be(expected);

    [Fact]
    public void The_namespace_axis_is_available_in_XPath()
    {
        StaticError("namespace::*", xpath: true).Should().BeNull();
        StaticError("namespace::*", xpath: false).Should().Contain("XQST0134");
    }
}
