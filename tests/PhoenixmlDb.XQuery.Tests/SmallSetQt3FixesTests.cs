using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Regression tests for the assorted small-set QT3 conformance fixes:
/// parameterized array-type member matching (xs:anyAtomicType excludes maps/arrays),
/// op:same-key exact numeric equality in map:remove, the implicit HTML5 DOCTYPE
/// gate, and lazy/short-circuiting general comparison over large ranges.
/// </summary>
public class SmallSetQt3FixesTests
{
    private readonly XQueryFacade _facade = new();

    // ── Parameterized array member-type matching ────────────────────────────
    // xs:anyAtomicType is NOT satisfied by maps or arrays (they are function
    // items, never atomic). prod-ArrayTest ArrayTest-059 / -079.

    [Fact]
    public async Task Array_with_map_member_is_not_array_of_anyAtomicType()
        => (await _facade.EvaluateAsync("['a','b','c','d','e', map{}] instance of array(xs:anyAtomicType+)"))
            .Should().Be("false");

    [Fact]
    public async Task Array_of_atomics_is_array_of_anyAtomicType()
        => (await _facade.EvaluateAsync("['a','b','c'] instance of array(xs:anyAtomicType+)"))
            .Should().Be("true");

    [Fact]
    public async Task Map_is_not_instance_of_anyAtomicType()
        => (await _facade.EvaluateAsync("map{} instance of xs:anyAtomicType"))
            .Should().Be("false");

    [Fact]
    public async Task Array_is_not_instance_of_anyAtomicType()
        => (await _facade.EvaluateAsync("[1,2] instance of xs:anyAtomicType"))
            .Should().Be("false");

    [Fact]
    public async Task Nested_string_array_type_still_matches()
        => (await _facade.EvaluateAsync(
                "let $f := function($a as array(array(xs:string*))) as xs:boolean {array:size($a) eq 4}, "
                + "$array:= [['a','b'],['c','d'], [], ['e']] return $f($array)"))
            .Should().Be("true");

    // ── op:same-key exact numeric equality in map:remove ────────────────────
    // map-remove-016: xs:decimal('1.0000000000100000000001') must NOT remove
    // the xs:double('1.00000000001') key — they are not the same mathematical value.

    [Fact]
    public async Task Map_remove_uses_exact_same_key_numeric_equality()
        => (await _facade.EvaluateAsync(
                "map{xs:float('1.0'):0, xs:double('1.00000000001'):1} "
                + "=> map:remove(xs:decimal('1.0000000000100000000001')) => map:size()"))
            .Should().Be("2");

    [Fact]
    public async Task Map_remove_still_removes_exact_numeric_key()
        => (await _facade.EvaluateAsync("map{1:'a', 2:'b'} => map:remove(1.0) => map:size()"))
            .Should().Be("1");

    // ── HTML5 implicit DOCTYPE gate ─────────────────────────────────────────
    // serialize-html-003: serializing a <body> fragment emits no DOCTYPE; only
    // a document/element whose outermost element is `html` gets <!DOCTYPE html>.

    [Fact]
    public async Task Html5_serialize_body_fragment_has_no_doctype()
        => (await _facade.EvaluateAsync(
                "let $doc := <html><head/><body><p>Hello World!</p></body></html> "
                + "return serialize($doc//body, map{'method':'html','html-version':5,'indent':false()})"))
            .Should().Be("<body><p>Hello World!</p></body>");

    [Fact]
    public async Task Html5_serialize_html_root_keeps_doctype()
        => (await _facade.EvaluateAsync(
                "serialize(<html><body><p>Hi</p></body></html>, "
                + "map{'method':'html','html-version':5,'indent':false()})"))
            .Should().StartWith("<!DOCTYPE html>");

    // ── Lazy / short-circuiting general comparison over large ranges ─────────
    // op-to RangeExpr-409d: `X < (A to B)` where the range spans billions of
    // integers must not materialise the whole range — it short-circuits on the
    // first matching member.

    [Fact]
    public async Task General_comparison_short_circuits_over_huge_range()
        => (await _facade.EvaluateAsync(
                "1000000000000000020001 < (1000000000000000000000 to 1000000000000500000003)"))
            .Should().Be("true");

    [Fact]
    public async Task General_comparison_basic_membership_holds()
        => (await _facade.EvaluateAsync("5 = (1 to 10)")).Should().Be("true");

    [Fact]
    public async Task General_comparison_no_member_matches_is_false()
        => (await _facade.EvaluateAsync("50 = (1 to 10)")).Should().Be("false");

    [Fact]
    public async Task General_comparison_empty_right_operand_is_false()
        => (await _facade.EvaluateAsync("5 = ()")).Should().Be("false");

    [Fact]
    public async Task General_comparison_empty_left_operand_is_false()
        => (await _facade.EvaluateAsync("() = (1 to 10)")).Should().Be("false");
}
