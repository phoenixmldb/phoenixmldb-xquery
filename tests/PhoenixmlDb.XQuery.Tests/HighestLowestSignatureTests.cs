using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Pins the XPath 4.0 §14.5 signature of fn:highest / fn:lowest:
///
///     fn:highest($input     as item()*,
///                $collation as xs:string?                         := fn:default-collation(),
///                $key       as (fn(item()) as xs:anyAtomicType*)? := fn:data#1) as item()*
///
/// The COLLATION is second and the key function third. This engine declared arity 1-2 with the
/// key in the second position, so highest#3 did not exist at all and highest($seq, $key) bound
/// a function into the collation slot — where it was read as a string, ignored, and silently
/// produced an unkeyed answer. Reported by Martin Honnen, 2026-08-23.
///
/// Separately, every key was coerced with Convert.ToDouble, so a sequence of strings threw an
/// unhandled System.FormatException and took the process down. Those crash cases are pinned
/// here too — they are the more serious half of the report, and they were not in it.
/// </summary>
public class HighestLowestSignatureTests
{
    private readonly XQueryFacade _facade = new();
    private async Task<string> Eval(string xq) => await _facade.EvaluateAsync(xq);

    private const string Codepoint = "http://www.w3.org/2005/xpath-functions/collation/codepoint";
    private const string CaseBlind = "http://www.w3.org/2005/xpath-functions/collation/caseblind";

    // --- the arity that did not exist ---

    [Fact]
    public async Task Highest_arity3_takes_the_key_as_third_argument()
        => (await Eval("string-join(fn:highest((1,5,3), (), fn($x) { -$x })!string(.), ',')"))
            .Should().Be("1");

    [Fact]
    public async Task Lowest_arity3_takes_the_key_as_third_argument()
        => (await Eval("string-join(fn:lowest((1,5,3), (), fn($x) { -$x })!string(.), ',')"))
            .Should().Be("5");

    [Fact]
    public async Task Highest_arity3_is_referenceable()
        => (await Eval("fn:highest#3 instance of function(*)")).Should().Be("true");

    [Fact]
    public async Task Lowest_arity3_is_referenceable()
        => (await Eval("fn:lowest#3 instance of function(*)")).Should().Be("true");

    // --- the second argument is a COLLATION, and it is honoured ---
    //
    // Under codepoint ordering 'a' (0x61) is above 'B' (0x42); ignoring case, "B" is above "a".
    // Getting different answers from the same input is what proves the argument is read at all
    // rather than accepted and discarded, which is what the old signature did.

    [Fact]
    public async Task Highest_arity2_uses_codepoint_collation()
        => (await Eval($"fn:highest(('a','B'), '{Codepoint}')")).Should().Be("a");

    [Fact]
    public async Task Highest_arity2_uses_case_blind_collation()
        => (await Eval($"fn:highest(('a','B'), '{CaseBlind}')")).Should().Be("B");

    [Fact]
    public async Task Empty_collation_falls_back_to_the_default()
        => (await Eval("fn:highest(('a','B'), ())")).Should().Be("a");

    // --- string keys must not crash the process ---

    [Fact]
    public async Task Highest_over_strings_does_not_throw()
        => (await Eval("fn:highest(('apple','banana','cherry'))")).Should().Be("cherry");

    [Fact]
    public async Task Lowest_over_strings_does_not_throw()
        => (await Eval("fn:lowest(('apple','banana','cherry'))")).Should().Be("apple");

    [Fact]
    public async Task Highest_over_dates_orders_chronologically()
        // string() rather than the bare value: adaptive output renders a date in constructor
        // notation, xs:date("2026-08-23"), and this test is about the ORDERING.
        => (await Eval("string(fn:highest((xs:date('2020-01-01'), xs:date('2026-08-23'), xs:date('2001-01-01'))))"))
            .Should().Be("2026-08-23");

    // --- behaviour that must not regress ---

    [Fact]
    public async Task Highest_returns_every_tied_item()
        => (await Eval("string-join(fn:highest((3,1,5,4,5))!string(.), ',')")).Should().Be("5,5");

    [Fact]
    public async Task Empty_input_gives_empty_sequence()
        => (await Eval("count(fn:highest(()))")).Should().Be("0");

    [Fact]
    public async Task Key_selects_by_a_computed_property()
        // The classic use: pick the longest word, not the alphabetically last one.
        => (await Eval("fn:highest(('aa','bbbb','c'), (), fn($s) { string-length($s) })"))
            .Should().Be("bbbb");

    [Fact]
    public async Task Key_may_select_a_map_field()
        => (await Eval("fn:highest((map{'n':1}, map{'n':9}), (), fn($m) { $m?n })?n"))
            .Should().Be("9");
}
