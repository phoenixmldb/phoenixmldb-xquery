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

    // ── Regression cluster (QT3 fn-min/max/sum/index-of/compare + app-Demos):
    // narrowing the derived-integer-tagging / UCA-collation / eq-via-comparer
    // changes so they stop over-applying to plain cases.

    // fn:index-of uses the eq operator, under which an xs:untypedAtomic operand is
    // cast to the dynamic type of the other operand — so it matches an xs:anyURI of
    // the same lexical value (K-SeqIndexOfFunc-17). The shared value comparer keeps
    // xs:anyURI distinct from strings (distinct-values semantics), so this case is
    // handled before delegating to it.
    [Fact]
    public async Task IndexOf_untypedAtomic_matches_anyURI()
        => (await _facade.EvaluateAsync(
                "index-of(xs:untypedAtomic('example.com/'), xs:anyURI('example.com/'))"))
            .Should().Be("1");

    // fn:sum of a single value returns that value unchanged, preserving its derived
    // integer type (K2-SeqSUMFunc-4).
    [Fact]
    public async Task Sum_of_single_unsignedShort_stays_unsignedShort()
        => (await _facade.EvaluateAsync("sum(xs:unsignedShort('1')) instance of xs:unsignedShort"))
            .Should().Be("true");

    [Fact]
    public async Task Sum_of_single_unsignedShort_value_is_correct()
        => (await _facade.EvaluateAsync("sum(xs:unsignedShort('41'))")).Should().Be("41");

    // fn:min / fn:max retain the winning value's derived integer type so the result
    // is an instance of that subtype and of its ancestors (fn-min-14/15, fn-max-14/15).
    [Fact]
    public async Task Min_retains_derived_integer_subtype()
        => (await _facade.EvaluateAsync(
                "min((xs:positiveInteger(123), xs:unsignedShort(124))) instance of xs:positiveInteger"))
            .Should().Be("true");

    [Fact]
    public async Task Min_result_is_instance_of_ancestor_type()
        => (await _facade.EvaluateAsync(
                "min((xs:positiveInteger(123), xs:unsignedShort(124))) instance of xs:nonNegativeInteger"))
            .Should().Be("true");

    [Fact]
    public async Task Max_retains_derived_integer_subtype()
        => (await _facade.EvaluateAsync(
                "max((xs:positiveInteger(123), xs:unsignedShort(124))) instance of xs:unsignedShort"))
            .Should().Be("true");

    // A predicate whose value is a derived-integer-typed variable (xs:positiveInteger,
    // now flowing as XsTypedInteger) is a numeric positional predicate, not an EBV-true
    // filter that keeps every node — otherwise xs:decimal(node-seq) throws (app-Demos
    // currencysvg InvalidCastException).
    [Fact]
    public async Task Derived_integer_variable_acts_as_positional_predicate()
    {
        const string q =
            "let $n := xs:positiveInteger(2) "
            + "return <r><a>x</a><b>y</b><c>z</c></r>/*[$n]/local-name()";
        (await _facade.EvaluateAsync(q)).Should().Be("b");
    }

    // UCA alternate=blanked normally ignores variable elements (the space), but at
    // strength=identical the full code points still distinguish the strings, so
    // compare(...) is non-zero (fn-compare-042).
    [Fact]
    public async Task Compare_uca_blanked_identical_distinguishes_space()
        => (await _facade.EvaluateAsync(
                "fn:compare('database', 'data base', "
                + "'http://www.w3.org/2013/collation/UCA?lang=en;alternate=blanked;strength=identical') = 0"))
            .Should().Be("false");

    // …while a lower strength with alternate=blanked still ignores the space.
    [Fact]
    public async Task Compare_uca_blanked_primary_ignores_space()
        => (await _facade.EvaluateAsync(
                "fn:compare('database', 'data base', "
                + "'http://www.w3.org/2013/collation/UCA?lang=en;alternate=blanked;strength=primary') = 0"))
            .Should().Be("true");
}
