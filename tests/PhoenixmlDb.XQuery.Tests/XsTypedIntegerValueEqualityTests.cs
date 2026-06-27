using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A derived-integer-typed value (xs:short, xs:positiveInteger, …, carried internally as
/// <c>XsTypedInteger</c>) must compare and hash BY VALUE everywhere it meets a bare
/// xs:integer of equal magnitude: value comparison, fn:distinct-values, fn:index-of,
/// fn:deep-equal, FLWOR <c>group by</c>, and map-key equality. The derived type tag is
/// still observable via <c>instance of</c> — only equality/hashing unwraps it.
/// Regression guard for QT3 cbcl-distinct-values-002b (XsTypedInteger tagging in
/// commit 9237753).
/// </summary>
public class XsTypedIntegerValueEqualityTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task Value_comparison_equates_derived_and_bare_integer()
    {
        var result = await _facade.EvaluateAsync("xs:short(1) eq 1");
        result.Should().Be("true");
    }

    [Fact]
    public async Task Distinct_values_collapses_derived_and_bare_integer()
    {
        // distinct-values((1, xs:short(1))) must yield a single item.
        var result = await _facade.EvaluateAsync("count(distinct-values((1, xs:short(1))))");
        result.Should().Be("1");
    }

    [Fact]
    public async Task Index_of_finds_derived_integer_by_bare_search()
    {
        var result = await _facade.EvaluateAsync("index-of((1, xs:short(2), 3), 2)");
        result.Should().Be("2");
    }

    [Fact]
    public async Task Deep_equal_across_derived_and_bare_integer()
    {
        var result = await _facade.EvaluateAsync("deep-equal((1, xs:short(2)), (1, 2))");
        result.Should().Be("true");
    }

    [Fact]
    public async Task Group_by_merges_derived_and_bare_integer_keys()
    {
        // The two items share a grouping key (1) despite differing tagged types,
        // so there is exactly one group.
        var result = await _facade.EvaluateAsync(
            "count(for $x in (1, xs:short(1)) group by $k := $x return $k)");
        result.Should().Be("1");
    }

    [Fact]
    public async Task Map_lookup_by_bare_integer_finds_derived_integer_key()
    {
        var result = await _facade.EvaluateAsync("map { xs:short(1) : 'hit' }(1)");
        result.Should().Be("hit");
    }
}
