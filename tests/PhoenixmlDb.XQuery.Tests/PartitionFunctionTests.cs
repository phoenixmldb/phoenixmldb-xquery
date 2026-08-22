using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// <c>fn:partition</c> (XPath 4.0). <c>$split</c> takes TWO arguments — the partition
/// accumulated so far and the next item — and returns true when that partition is COMPLETE,
/// so the next item begins a new one. The result is a sequence of arrays.
///
/// This was previously implemented as "split into runs where a one-argument predicate has the
/// same value", which is a different function. Reported by Martin Honnen on 2026-08-22:
/// <c>partition(1 to 7, function($p,$n) { count($p) eq 2 })</c> should give four arrays
/// ([1,2] [3,4] [5,6] [7]) but returned a single group, because the predicate was invoked with
/// one argument so <c>count($p)</c> was never 2 and nothing ever split.
/// </summary>
public class PartitionFunctionTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task Splits_when_the_partition_reaches_a_size()
    {
        // Martin's case. Saxon EE 13 gives [1,2] [3,4] [5,6] [7].
        var count = await _facade.EvaluateAsync(
            "partition(1 to 7, function($p, $n) { count($p) eq 2 }) => count()", "<x/>");
        count.Should().Be("4");
    }

    [Fact]
    public async Task Each_partition_is_an_array()
    {
        var sizes = await _facade.EvaluateAsync(
            "string-join(partition(1 to 7, function($p, $n) { count($p) eq 2 }) ! string(array:size(.)), ',')",
            "<x/>");
        sizes.Should().Be("2,2,2,1");
    }

    [Fact]
    public async Task Members_land_in_the_right_partitions()
    {
        var first = await _facade.EvaluateAsync(
            "string-join(partition(1 to 7, function($p, $n) { count($p) eq 2 })[1] ! array:flatten(.) ! string(), ',')",
            "<x/>");
        first.Should().Be("1,2");
    }

    [Fact]
    public async Task Splits_on_a_change_of_value()
    {
        // The other common shape: close the partition when the next item differs from the last
        // one collected. (1,1,2,2,3) -> [1,1] [2,2] [3].
        var count = await _facade.EvaluateAsync(
            "partition((1,1,2,2,3), function($p, $n) { $p[last()] ne $n }) => count()", "<x/>");
        count.Should().Be("3");
    }

    [Fact]
    public async Task Empty_input_yields_no_partitions()
    {
        var count = await _facade.EvaluateAsync(
            "count(partition((), function($p, $n) { true() }))", "<x/>");
        count.Should().Be("0");
    }

    [Fact]
    public async Task A_split_that_never_fires_yields_one_partition()
    {
        var count = await _facade.EvaluateAsync(
            "partition(1 to 3, function($p, $n) { false() }) => count()", "<x/>");
        count.Should().Be("1");
    }

    [Fact]
    public async Task The_split_never_sees_an_empty_partition()
    {
        // $split is only consulted once something has been collected, so a predicate that
        // returns true unconditionally still puts one item in each partition rather than
        // emitting an empty leading one.
        var count = await _facade.EvaluateAsync(
            "partition(1 to 4, function($p, $n) { true() }) => count()", "<x/>");
        count.Should().Be("4");
    }
}
