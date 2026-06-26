using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Regression tests for F&amp;O builtin fixes surfaced by the QT3 functx/Walmsley
/// application test sets (real-world queries from Priscilla Walmsley's functx
/// library). Each group documents the shared root cause it pins down.
/// </summary>
public class FunctxAppFixesTests
{
    private readonly XQueryFacade _facade = new();

    // ── Cluster 1: a plain integer literal has dynamic type xs:integer and is
    // NOT an instance of any narrower derived subtype (xs:byte, xs:short, …).
    // (functx:atomic-type(2) must report "xs:integer", not "xs:byte".)

    [Fact]
    public async Task Integer_literal_is_not_instance_of_byte()
        => (await _facade.EvaluateAsync("2 instance of xs:byte")).Should().Be("false");

    [Fact]
    public async Task Integer_literal_is_not_instance_of_short()
        => (await _facade.EvaluateAsync("2 instance of xs:short")).Should().Be("false");

    [Fact]
    public async Task Integer_literal_is_not_instance_of_nonNegativeInteger()
        => (await _facade.EvaluateAsync("2 instance of xs:nonNegativeInteger")).Should().Be("false");

    [Fact]
    public async Task Integer_literal_is_instance_of_integer()
        => (await _facade.EvaluateAsync("2 instance of xs:integer")).Should().Be("true");

    [Fact]
    public async Task XsInteger_cast_is_not_instance_of_byte()
        => (await _facade.EvaluateAsync("xs:integer(2) instance of xs:byte")).Should().Be("false");

    // Derived constructors stay strict instances of their own (and ancestor) types.

    [Fact]
    public async Task XsByte_is_instance_of_byte()
        => (await _facade.EvaluateAsync("xs:byte(2) instance of xs:byte")).Should().Be("true");

    [Fact]
    public async Task XsByte_is_instance_of_integer()
        => (await _facade.EvaluateAsync("xs:byte(2) instance of xs:integer")).Should().Be("true");

    [Fact]
    public async Task XsByte_is_not_instance_of_short_unless_ancestor()
        => (await _facade.EvaluateAsync("xs:byte(2) instance of xs:short")).Should().Be("true");

    [Fact]
    public async Task XsShort_is_not_instance_of_byte()
        => (await _facade.EvaluateAsync("xs:short(2) instance of xs:byte")).Should().Be("false");

    [Fact]
    public async Task XsPositiveInteger_is_instance_of_nonNegativeInteger()
        => (await _facade.EvaluateAsync("xs:positiveInteger(1) instance of xs:nonNegativeInteger")).Should().Be("true");

    [Fact]
    public async Task XsNonNegativeInteger_is_not_instance_of_positiveInteger()
        => (await _facade.EvaluateAsync("xs:nonNegativeInteger(0) instance of xs:positiveInteger")).Should().Be("false");

    // ── Cluster 2: fn:index-of compares with the eq operator, so a member that
    // differs in numeric subtype from $search still matches (4 eq 04.0).

    [Fact]
    public async Task IndexOf_matches_across_numeric_subtypes()
        => (await _facade.EvaluateAsync("string-join(for $i in index-of((4, 5, 6, 4), 04.0) return string($i), ' ')"))
            .Should().Be("1 4");

    [Fact]
    public async Task IndexOf_string_matches()
        => (await _facade.EvaluateAsync("string-join(for $i in index-of(('a','b','c'), 'a') return string($i), ' ')"))
            .Should().Be("1");

    [Fact]
    public async Task IndexOf_integer_search_still_matches_integers()
        => (await _facade.EvaluateAsync("string-join(for $i in index-of((4, 5, 6, 4), 4) return string($i), ' ')"))
            .Should().Be("1 4");

    // ── Cluster 3: function-conversion of node arguments to an xs:anyAtomicType*
    // parameter must keep xs:untypedAtomic (not collapse to xs:string), so a
    // downstream fn:sum can cast it to xs:double rather than erroring XPTY0004.

    // ── Cluster 1 follow-through: derived-integer constructors now flow as
    // XsTypedInteger; the JSON output method must still render them as bare
    // integers (QT3 Serialization-json-10/18).

    [Fact]
    public async Task Json_output_renders_derived_integers_as_bare_numbers()
    {
        const string q = "declare namespace output = \"http://www.w3.org/2010/xslt-xquery-serialization\"; "
            + "declare option output:method \"json\"; "
            + "[12, 12.34, xs:int(\"45\"), xs:decimal(\"45.67\"), xs:unsignedShort(\"89\")]";
        (await _facade.EvaluateAsync(q)).Should().Be("[12,12.34,45,45.67,89]");
    }

    [Fact]
    public async Task Sum_of_attributes_passed_through_anyAtomicType_param()
    {
        const string q = "declare function local:f($values as xs:anyAtomicType*) as xs:double "
            + "{ sum($values[string(.) != '']) }; "
            + "let $in := <prices><price discount=\"10.00\"/><price discount=\"6.00\"/>"
            + "<price/><price discount=\"\"/></prices> "
            + "return local:f($in//price/@discount)";
        (await _facade.EvaluateAsync(q)).Should().Be("1.6e1");
    }
}
