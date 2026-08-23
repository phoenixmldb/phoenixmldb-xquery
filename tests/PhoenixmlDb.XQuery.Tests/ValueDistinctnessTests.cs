using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:all-equal, fn:all-different and fn:duplicate-values (XPath 4.0), each of which takes an
/// optional collation as its second argument:
///
///     fn:all-equal($input as xs:anyAtomicType*,
///                  $collation as xs:string? := fn:default-collation()) as xs:boolean
///
/// None of the three had the arity-2 form, and none had any tests. All three compared
/// .ToString() of the atomized value, so values of different types compared equal whenever
/// their lexical forms matched — all-equal((1,"1")) was true and all-different((1,"1")) was
/// false, both backwards.
///
/// Found by auditing every collation-taking function after Martin Honnen reported the same
/// class of defect in fn:highest/fn:lowest. He did not report these.
/// </summary>
public class ValueDistinctnessTests
{
    private readonly XQueryFacade _facade = new();
    private async Task<string> Eval(string xq) => await _facade.EvaluateAsync(xq);

    private const string Codepoint = "http://www.w3.org/2005/xpath-functions/collation/codepoint";
    private const string CaseBlind = "http://www.w3.org/2005/xpath-functions/collation/caseblind";

    // --- the type confusion ---

    [Fact]
    public async Task AllEqual_does_not_conflate_an_integer_with_its_lexical_form()
        => (await Eval("fn:all-equal((1, '1'))")).Should().Be("false");

    [Fact]
    public async Task AllDifferent_does_not_conflate_an_integer_with_its_lexical_form()
        => (await Eval("fn:all-different((1, '1'))")).Should().Be("true");

    [Fact]
    public async Task DuplicateValues_does_not_conflate_an_integer_with_its_lexical_form()
        => (await Eval("count(fn:duplicate-values((1, '1')))")).Should().Be("0");

    // Numeric promotion still applies: 1 and 1.0 ARE the same value.
    [Fact]
    public async Task AllEqual_treats_promoted_numerics_as_equal()
        => (await Eval("fn:all-equal((1, 1.0, 1e0))")).Should().Be("true");

    // --- the arity that did not exist, and proof the collation is read ---

    [Fact]
    public async Task AllEqual_arity2_under_case_blind_collation()
        => (await Eval($"fn:all-equal(('a','A'), '{CaseBlind}')")).Should().Be("true");

    [Fact]
    public async Task AllEqual_arity2_under_codepoint_collation()
        => (await Eval($"fn:all-equal(('a','A'), '{Codepoint}')")).Should().Be("false");

    [Fact]
    public async Task AllDifferent_arity2_under_case_blind_collation()
        => (await Eval($"fn:all-different(('a','A'), '{CaseBlind}')")).Should().Be("false");

    [Fact]
    public async Task AllDifferent_arity2_under_codepoint_collation()
        => (await Eval($"fn:all-different(('a','A'), '{Codepoint}')")).Should().Be("true");

    [Fact]
    public async Task DuplicateValues_arity2_under_case_blind_collation()
        => (await Eval($"string-join(fn:duplicate-values(('a','A','b'), '{CaseBlind}'), ',')"))
            .Should().Be("a");

    [Fact]
    public async Task DuplicateValues_arity2_under_codepoint_collation()
        => (await Eval($"count(fn:duplicate-values(('a','A','b'), '{Codepoint}'))")).Should().Be("0");

    [Fact]
    public async Task Empty_collation_falls_back_to_the_default()
        => (await Eval("fn:all-equal(('a','A'), ())")).Should().Be("false");

    // --- base behaviour ---

    [Fact]
    public async Task AllEqual_of_empty_is_true()
        => (await Eval("fn:all-equal(())")).Should().Be("true");

    [Fact]
    public async Task AllEqual_of_singleton_is_true()
        => (await Eval("fn:all-equal(42)")).Should().Be("true");

    [Fact]
    public async Task AllDifferent_of_empty_is_true()
        => (await Eval("fn:all-different(())")).Should().Be("true");

    [Fact]
    public async Task AllEqual_detects_a_late_mismatch()
        => (await Eval("fn:all-equal((7,7,7,8))")).Should().Be("false");

    [Fact]
    public async Task DuplicateValues_reports_each_repeated_value_once()
        => (await Eval("string-join(fn:duplicate-values((1,2,1,3,2,1))!string(.), ',')"))
            .Should().Be("1,2");

    [Fact]
    public async Task DuplicateValues_of_all_distinct_is_empty()
        => (await Eval("count(fn:duplicate-values((1,2,3)))")).Should().Be("0");

    [Fact]
    public async Task DuplicateValues_returns_values_not_repeat_counts()
        // A value appearing three times is still reported exactly once.
        => (await Eval("count(fn:duplicate-values((5,5,5)))")).Should().Be("1");
}
