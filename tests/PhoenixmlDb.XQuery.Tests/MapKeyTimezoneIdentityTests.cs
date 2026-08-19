using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A date/time value WITH a timezone and one WITHOUT are never the same map key, whatever
/// their instants. op:same-key is deliberately finer than op:eq: QT3 same-key-013 requires
/// <c>map:size eq 3</c> over a sequence where <c>distinct-values</c> and <c>group-by</c> both
/// yield fewer than 3.
///
/// The comparer previously compared instants alone, reinterpreting a missing timezone as Z.
/// That collapses the pair only when the implicit timezone IS UTC — the one offset for which
/// "reinterpret the wall clock as Z" is the identity — so it passed on every developer machine
/// outside UTC and failed on CI, which runs UTC: W3C xslt30-test maps-010, "mix values with
/// and without timezones" (bug 28632), reported a duplicate key in a map constructor.
///
/// Every assertion below uses LITERAL values chosen so the collision does not depend on the
/// ambient timezone. The first attempt at these tests set the TZ environment variable per
/// case; that does not change what .NET reports mid-process, so the tests passed against the
/// unfixed comparer and proved nothing. Pinning a timezone-dependent defect with a
/// timezone-dependent test is how it survived in the first place — the literals are the point.
/// </summary>
public class MapKeyTimezoneIdentityTests
{
    private readonly XQueryFacade _facade = new();

    private static string MapSizeOver(params string[] keys) =>
        $"map:size(map:merge(({string.Join(", ", keys)}) ! map:entry(., 1)))";

    /// <summary>
    /// The reported failure. Under instant-only comparison the untimezoned value is reread as
    /// 01:30Z and collides with the explicit 01:30Z, whatever zone the machine is in.
    /// </summary>
    [Fact]
    public async Task DateTimeWithAndWithoutTimezone_AreDistinctMapKeys() =>
        (await _facade.EvaluateAsync(MapSizeOver(
            "xs:dateTime('2015-04-08T01:30:00')", "xs:dateTime('2015-04-08T01:30:00Z')")))
        .Should().Be("2", "a timezoned and a non-timezoned dateTime are distinct keys");

    [Fact]
    public async Task DateWithAndWithoutTimezone_AreDistinctMapKeys() =>
        (await _facade.EvaluateAsync(MapSizeOver(
            "xs:date('2015-04-08')", "xs:date('2015-04-08Z')")))
        .Should().Be("2", "xs:date follows the same rule");

    [Fact]
    public async Task TimeWithAndWithoutTimezone_AreDistinctMapKeys() =>
        (await _facade.EvaluateAsync(MapSizeOver(
            "xs:time('01:30:00')", "xs:time('01:30:00Z')")))
        .Should().Be("2", "xs:time follows the same rule");

    /// <summary>
    /// The other side of the rule, and the thing the fix must not break: two values that BOTH
    /// carry a timezone and denote one instant remain a single key.
    /// </summary>
    [Fact]
    public async Task SameInstantInDifferentZones_IsStillOneKey() =>
        (await _facade.EvaluateAsync(MapSizeOver(
            "xs:dateTime('2015-04-08T01:30:00Z')", "xs:dateTime('2015-04-07T21:30:00-04:00')")))
        .Should().Be("1", "both are timezoned and denote one instant, so they are one key");

    /// <summary>Two untimezoned values with the same wall clock are also one key.</summary>
    [Fact]
    public async Task SameWallClockWithoutTimezone_IsOneKey() =>
        (await _facade.EvaluateAsync(MapSizeOver(
            "xs:dateTime('2015-04-08T01:30:00')", "xs:dateTime('2015-04-08T01:30:00')")))
        .Should().Be("1", "neither carries a timezone and the wall clocks match");

    /// <summary>
    /// same-key-013's contrast. This one derives the timezoned member with
    /// <c>adjust-dateTime-to-timezone($w, implicit-timezone())</c>, exactly as the suite does,
    /// because that is what makes the pair denote one instant in EVERY zone — a literal 'Z'
    /// only coincides with the untimezoned value on a UTC machine, which is what made the
    /// first draft of this test fail here in EDT.
    ///
    /// Note the division of labour: the literal-based tests above are what CATCH the defect in
    /// any zone. This one documents the rule the defect violated — map keys separate what
    /// distinct-values merges — and only distinguishes fixed from unfixed under UTC.
    /// </summary>
    [Fact]
    public async Task MapKeysSeparate_WhereDistinctValuesMerges()
    {
        const string keys = "let $w := xs:dateTime('2015-04-08T01:30:00'), " +
                            "    $t := adjust-dateTime-to-timezone($w, implicit-timezone()), " +
                            "    $keys := (xs:dateTime('2015-04-08T02:30:00'), $w, $t) return ";

        (await _facade.EvaluateAsync(keys + "map:size(map:merge($keys ! map:entry(., 1)))"))
            .Should().Be("3", "all three are distinct map keys");

        (await _facade.EvaluateAsync(keys + "count(distinct-values($keys))"))
            .Should().Be("2", "distinct-values applies the implicit timezone, merging what map keys separate");
    }
}
