using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The implicit timezone is a setting of the query (<see cref="QueryEngine.ImplicitTimezone"/>,
/// <see cref="QueryExecutionContext.ImplicitTimezone"/>). It was the timezone of the machine in
/// every place, so the same query on the same data gave different answers on two machines.
/// </summary>
public class ImplicitTimezoneSettingTests
{
    private static readonly TimeSpan East = TimeSpan.FromHours(5);
    private static readonly TimeSpan West = TimeSpan.FromHours(-8);

    private static async Task<string> Eval(string query, TimeSpan? zone)
    {
        var engine = new QueryEngine { ImplicitTimezone = zone };
        var items = new List<string>();
        await foreach (var item in engine.ExecuteAsync(query))
            items.Add(item switch { bool b => b ? "true" : "false", null => "", _ => item.ToString()! });
        return string.Join(" ", items);
    }

    // Each is true at +05:00 and false at -08:00: noon with no timezone is 07:00Z in the east
    // and 20:00Z in the west, and the other operand is between the two.
    [Theory]
    [InlineData("xs:dateTime('2024-03-01T12:00:00') lt xs:dateTime('2024-03-01T12:00:00Z')")]
    [InlineData("xs:dateTime('2024-03-01T12:00:00') eq xs:dateTime('2024-03-01T07:00:00Z')")]
    [InlineData("xs:time('12:00:00') eq xs:time('07:00:00Z')")]
    [InlineData("xs:date('2024-03-01') eq xs:date('2024-03-01+05:00')")]
    [InlineData("xs:dateTime('2024-03-01T12:00:00') - xs:dateTime('2024-03-01T07:00:00Z') eq xs:dayTimeDuration('PT0S')")]
    [InlineData("xs:time('12:00:00') - xs:time('07:00:00Z') eq xs:dayTimeDuration('PT0S')")]
    [InlineData("xs:date('2024-03-01') - xs:date('2024-03-01+05:00') eq xs:dayTimeDuration('PT0S')")]
    [InlineData("xs:gYear('2024') eq xs:gYear('2024+05:00')")]
    [InlineData("deep-equal(xs:dateTime('2024-03-01T12:00:00'), xs:dateTime('2024-03-01T07:00:00Z'))")]
    [InlineData("count(distinct-values((xs:dateTime('2024-03-01T12:00:00'), xs:dateTime('2024-03-01T07:00:00Z')))) eq 1")]
    [InlineData("count(for $d in (xs:dateTime('2024-03-01T12:00:00'), xs:dateTime('2024-03-01T07:00:00Z')) group by $k := $d return 1) eq 1")]
    [InlineData("max((xs:dateTime('2024-03-01T12:00:00'), xs:dateTime('2024-03-01T12:00:00Z'))) eq xs:dateTime('2024-03-01T12:00:00Z')")]
    [InlineData("(for $d in (xs:dateTime('2024-03-01T12:00:00Z'), xs:dateTime('2024-03-01T12:00:00')) order by $d return string($d))[1] eq '2024-03-01T12:00:00'")]
    [InlineData("string(adjust-dateTime-to-timezone(xs:dateTime('2024-03-01T12:00:00'))) eq '2024-03-01T12:00:00+05:00'")]
    [InlineData("string(adjust-date-to-timezone(xs:date('2024-03-01'))) eq '2024-03-01+05:00'")]
    [InlineData("string(adjust-time-to-timezone(xs:time('12:00:00'))) eq '12:00:00+05:00'")]
    [InlineData("xs:dateTime('2024-03-01T12:00:00') = xs:dateTime('2024-03-01T07:00:00Z')")]
    [InlineData("exists(index-of((xs:dateTime('2024-03-01T07:00:00Z')), xs:dateTime('2024-03-01T12:00:00')))")]
    [InlineData("string(sort((xs:dateTime('2024-03-01T12:00:00Z'), xs:dateTime('2024-03-01T12:00:00')))[1]) eq '2024-03-01T12:00:00'")]
    [InlineData("string(min((xs:dateTime('2024-03-01T12:00:00Z'), xs:dateTime('2024-03-01T12:00:00')))) eq '2024-03-01T12:00:00'")]
    [InlineData("string(min((xs:time('12:00:00Z'), xs:time('12:00:00')))) eq '12:00:00'")]
    [InlineData("string(max((xs:date('2024-03-01Z'), xs:date('2024-03-01')))) eq '2024-03-01Z'")]
    [InlineData("xs:date('2024-03-01') lt xs:date('2024-03-01Z')")]
    [InlineData("xs:time('12:00:00') lt xs:time('12:00:00Z')")]
    [InlineData("xs:gYearMonth('2024-03') eq xs:gYearMonth('2024-03+05:00')")]
    [InlineData("xs:gMonthDay('--03-01') eq xs:gMonthDay('--03-01+05:00')")]
    [InlineData("xs:gDay('---01') eq xs:gDay('---01+05:00')")]
    [InlineData("xs:gMonth('--03') eq xs:gMonth('--03+05:00')")]
    [InlineData("some $x in (xs:dateTime('2024-03-01T12:00:00')) satisfies $x eq xs:dateTime('2024-03-01T07:00:00Z')")]
    [InlineData("(xs:dateTime('2024-03-01T12:00:00'), 1)[. instance of xs:dateTime][. eq xs:dateTime('2024-03-01T07:00:00Z')] instance of xs:dateTime")]
    [InlineData("implicit-timezone() eq xs:dayTimeDuration('PT5H')")]
    [InlineData("timezone-from-dateTime(current-dateTime()) eq xs:dayTimeDuration('PT5H')")]
    [InlineData("timezone-from-date(current-date()) eq xs:dayTimeDuration('PT5H')")]
    [InlineData("timezone-from-time(current-time()) eq xs:dayTimeDuration('PT5H')")]
    public async Task The_setting_decides(string query)
    {
        (await Eval(query, East)).Should().Be("true");
        (await Eval(query, West)).Should().Be("false");
    }

    // Ordered by value and not by the text of each value.
    [Theory]
    [InlineData("for $d in (xs:dayTimeDuration('P10D'), xs:dayTimeDuration('P9D')) order by $d return string($d)", "P9D P10D")]
    [InlineData("for $d in (xs:yearMonthDuration('P10Y'), xs:yearMonthDuration('P9Y')) order by $d return string($d)", "P9Y P10Y")]
    [InlineData("for $d in (xs:dateTime('2024-03-01T12:00:00+02:00'), xs:dateTime('2024-03-01T11:00:00Z')) order by $d return string($d)", "2024-03-01T12:00:00+02:00 2024-03-01T11:00:00Z")]
    [InlineData("for $d in (xs:time('12:00:00+02:00'), xs:time('11:00:00Z')) order by $d return string($d)", "12:00:00+02:00 11:00:00Z")]
    [InlineData("for $d in (xs:date('2024-03-01-10:00'), xs:date('2024-03-01+10:00')) order by $d return string($d)", "2024-03-01+10:00 2024-03-01-10:00")]
    public async Task Order_by_compares_dates_times_and_durations_by_value(string query, string expected)
        => (await Eval(query, TimeSpan.Zero)).Should().Be(expected);

    [Fact]
    public async Task The_current_instant_is_the_same_instant_in_the_zone_that_was_set()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var text = await Eval("string(adjust-dateTime-to-timezone(current-dateTime(), xs:dayTimeDuration('PT0S')))", East);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture).Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task With_nothing_set_it_is_the_timezone_of_the_machine()
    {
        var machine = DateTimeOffset.Now.Offset;
        var sign = machine < TimeSpan.Zero ? "-" : "";
        var duration = $"{sign}PT{Math.Abs((int)machine.TotalMinutes)}M";
        (await Eval($"implicit-timezone() eq xs:dayTimeDuration('{duration}')", null)).Should().Be("true");
    }

    [Fact]
    public async Task With_nothing_set_on_the_engine_it_is_what_the_host_set_around_the_call()
    {
        using (PhoenixmlDb.Xdm.ImplicitTimezone.Use(East))
        {
            (await Eval("implicit-timezone() eq xs:dayTimeDuration('PT5H')", null)).Should().Be("true");
            (await Eval("xs:time('12:00:00') eq xs:time('07:00:00Z')", null)).Should().Be("true");
        }
    }

    [Fact]
    public async Task Queries_that_run_side_by_side_each_have_their_own()
    {
        // Each query gives many items, and the two are read in turn, item by item, by one
        // caller: what one sets must not be in effect for the other.
        const string query = "for $i in 1 to 200 return xs:dateTime('2024-03-01T12:00:00') eq xs:dateTime('2024-03-01T07:00:00Z')";
        var east = new QueryEngine { ImplicitTimezone = East }.ExecuteAsync(query).GetAsyncEnumerator();
        var west = new QueryEngine { ImplicitTimezone = West }.ExecuteAsync(query).GetAsyncEnumerator();
        await using (east)
        await using (west)
        {
            for (var i = 0; i < 200; i++)
            {
                (await east.MoveNextAsync()).Should().BeTrue();
                (await west.MoveNextAsync()).Should().BeTrue();
                east.Current.Should().Be(true, $"item {i} of the query in the east");
                west.Current.Should().Be(false, $"item {i} of the query in the west");
                PhoenixmlDb.Xdm.ImplicitTimezone.Configured.Should().BeNull("a query leaves nothing set for its caller");
            }
        }
    }

    [Fact]
    public async Task Many_queries_on_many_threads_each_have_their_own()
    {
        var tasks = Enumerable.Range(0, 64).Select(i => Task.Run(async () =>
        {
            var zone = TimeSpan.FromHours(i % 2 == 0 ? 5 : -8);
            return (i, await Eval("for $i in 1 to 50 return xs:time('12:00:00') eq xs:time('07:00:00Z')", zone));
        }));
        foreach (var (i, result) in await Task.WhenAll(tasks))
            result.Should().Be(string.Join(" ", Enumerable.Repeat(i % 2 == 0 ? "true" : "false", 50)));
    }

    [Fact]
    public async Task One_compiled_plan_runs_in_each_zone()
    {
        // Nothing of the zone is fixed when the query is compiled.
        var engine = new QueryEngine();
        var plan = engine.Compile("xs:dateTime('2024-03-01T12:00:00') eq xs:dateTime('2024-03-01T07:00:00Z')").ExecutionPlan!;
        async Task<object?> Run(TimeSpan zone)
        {
            using var context = engine.CreateContext();
            context.ImplicitTimezone = zone;
            object? last = null;
            await foreach (var item in plan.ExecuteAsync(context))
                last = item;
            return last;
        }
        (await Run(East)).Should().Be(true);
        (await Run(West)).Should().Be(false);
        (await Run(East)).Should().Be(true);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(-15)]
    public void A_zone_outside_the_range_of_XML_Schema_is_refused(int hours)
    {
        var engine = new QueryEngine();
        using var context = engine.CreateContext();
        var set = () => context.ImplicitTimezone = TimeSpan.FromHours(hours);
        set.Should().Throw<ArgumentOutOfRangeException>();
    }
}
