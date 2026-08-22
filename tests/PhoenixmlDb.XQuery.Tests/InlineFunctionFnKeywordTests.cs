using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// XPath/XQuery 4.0 §4.6.6: "The keywords <c>function</c> and <c>fn</c> are synonymous."
/// So <c>fn($x, $y) { $x + $y }</c> and <c>fn() { … }</c> are inline function expressions.
///
/// Requested by Martin Honnen (2026-08-22): the 4.0 specs and the Saxon/BaseX examples use the
/// short form throughout, so examples could not be run against this engine unchanged.
///
/// The hazard this carries is that <c>fn</c> is ALSO the standard namespace prefix. Making it a
/// lexer keyword would stop <c>fn:count(…)</c> lexing as a prefixed name and break every call in
/// the standard namespace, so <c>KW_FN</c> is included in the parser's <c>ncName</c> rule — the
/// same treatment the grammar already gives its other contextual keywords. The tests that keep
/// <c>fn</c> working as a NAME are therefore the important half of this file, not an afterthought.
///
/// NOT implemented, and deliberately so: the no-parens FOCUS FUNCTION form
/// (<c>fn { @vat + @price }</c>, §4.6.6.1), which is arity-1 with the argument bound to the
/// context value. That is a separate feature — it applies to <c>function { … }</c> equally — and
/// treating it as a zero-arity function would be silently wrong.
/// </summary>
public class InlineFunctionFnKeywordTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("fn($x) { $x * 2 }(21)", "42")]
    [InlineData("fn($a, $b) { $a || $b }('ab','cd')", "abcd")]
    [InlineData("fn() { 42 }()", "42")]
    [InlineData("fn($x as xs:integer) as xs:integer { $x + 1 }(1)", "2")]
    public async Task Fn_is_a_synonym_for_function(string query, string expected)
    {
        var result = await _facade.EvaluateAsync(query, "<x/>");
        result.Should().Be(expected);
    }

    [Fact]
    public async Task Function_keyword_still_works()
    {
        var result = await _facade.EvaluateAsync("function($x) { $x + 1 }(41)", "<x/>");
        result.Should().Be("42");
    }

    [Theory]
    [InlineData("fn:count((1,2,3))", "3")]
    [InlineData("fn:string-join(('a','b'),'-')", "a-b")]
    [InlineData("fn:upper-case('ab')", "AB")]
    public async Task The_fn_namespace_prefix_still_resolves(string query, string expected)
    {
        // The regression this guards: a bare `fn` keyword token would make these parse errors.
        var result = await _facade.EvaluateAsync(query, "<x/>");
        result.Should().Be(expected);
    }

    [Fact]
    public async Task Fn_still_works_as_a_variable_name()
    {
        var result = await _facade.EvaluateAsync("let $fn := 7 return $fn", "<x/>");
        result.Should().Be("7");
    }

    [Fact]
    public async Task Fn_still_works_as_an_element_name()
    {
        var result = await _facade.EvaluateAsync("<fn>hi</fn>", "<x/>");
        result.Should().Contain("<fn>hi</fn>");
    }

    [Fact]
    public async Task Fn_still_works_as_a_declared_prefix()
    {
        var result = await _facade.EvaluateAsync(
            "declare namespace fn = 'http://www.w3.org/2005/xpath-functions'; fn:count((1,2))",
            "<x/>");
        result.Should().Be("2");
    }

    [Fact]
    public async Task Short_lambda_works_as_a_higher_order_argument()
    {
        // Martin's own case, in the short form he asked for.
        var result = await _facade.EvaluateAsync(
            "partition(1 to 7, fn($p, $n) { count($p) eq 2 }) => count()", "<x/>");
        result.Should().Be("4");
    }
}
