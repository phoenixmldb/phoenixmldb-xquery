using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// FLWOR <c>group by</c> finds the group of a tuple by the hash of its key. A search of every
/// group for every tuple made n different keys cost n * n comparisons (QT3 Catalog007). The
/// groups, their order and their content must be what that search gave.
/// </summary>
public class GroupByKeyTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    // Numeric keys of different types that are equal are one group.
    [InlineData("for $x in (1, 1.0, 1e0, xs:float(1), 2) group by $k := $x return count($x)", "4 1")]
    // A string and an xs:untypedAtomic with the same characters are one group; a number is not.
    [InlineData("for $x in ('1', xs:untypedAtomic('1'), 1) group by $k := $x return count($x)", "2 1")]
    // xs:anyURI is of the string family.
    [InlineData("for $x in ('a', xs:anyURI('a'), xs:token('a'), 'b') group by $k := $x return count($x)", "3 1")]
    // The empty sequence is a key of its own, and NaN is equal to NaN here.
    [InlineData("for $x in (1, 2, 3, 4) group by $k := (if ($x mod 2 = 0) then () else 'odd') return count($x)", "2 2")]
    [InlineData("for $x in (xs:double('NaN'), xs:float('NaN'), 1) group by $k := $x return count($x)", "2 1")]
    // The same instant in two timezones.
    [InlineData("for $x in (xs:dateTime('2020-01-01T12:00:00Z'), xs:dateTime('2020-01-01T13:00:00+01:00')) group by $k := $x return count($x)", "2")]
    // A collation that ignores case, on the spec.
    [InlineData("for $x in ('a', 'A', 'b') group by $k := $x collation 'http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive' return count($x)", "2 1")]
    // Two keys: the groups are in the order of their first tuple.
    [InlineData("for $x in (1 to 6) group by $a := $x mod 2, $b := $x mod 3 return string-join(($a, $b, count($x)) ! string(), '.')", "1.1.1 0.2.1 1.0.1 0.1.1 1.2.1 0.0.1")]
    [InlineData("for $x in ('c', 'a', 'b', 'a', 'c') group by $k := $x return $k || count($x)", "c2 a2 b1")]
    public async Task The_groups_are_those_of_the_value_comparison(string query, string expected)
        => (await _facade.EvaluateAsync($"string-join(({query}) ! string(), ' ')")).Should().Be(expected);

    [Fact]
    public async Task A_grouping_with_many_different_keys_does_not_compare_every_pair()
    {
        // 150,000 different keys: 11 thousand million comparisons when each tuple searched every
        // group, which is minutes. The bound is wide for a slow machine and still far below that.
        var sw = Stopwatch.StartNew();
        var run = Task.Run(() => _facade.EvaluateAsync(
            "count(for $x in (1 to 150000) ! ('k' || .) group by $k := $x return $k)"));
        var finished = await Task.WhenAny(run, Task.Delay(30_000));

        finished.Should().BeSameAs(run, $"the grouping was still running after {sw.ElapsedMilliseconds} ms");
        (await run).Should().Be("150000");
    }
}
