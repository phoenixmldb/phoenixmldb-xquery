using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// <see cref="QueryExecutionLimits.MaxStringLength"/>. An expression can build a very large string
/// without reading any input, faster than a time limit can cancel it. Each operation that builds
/// a string checks the limit before it builds the string, or while it builds it, and fails with
/// FOER0000.
/// </summary>
public sealed class MaxStringLengthTests
{
    private const int Limit = 1_000;

    internal static async Task<string> RunAsync(string query, int? maxStringLength)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var context = engine.CreateContext(limits: new QueryExecutionLimits { MaxStringLength = maxStringLength });
        var results = new List<string>();
        try
        {
            await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(context))
                results.Add(item is PhoenixmlDb.Xdm.Nodes.XdmNode node
                    ? node.StringValue.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? "");
        }
        catch (XQueryRuntimeException ex)
        {
            return ex.ErrorCode + ": " + ex.Message;
        }
        return string.Join(",", results);
    }

    // Each query builds a string of about 2,000 characters. Below, the same query with a limit
    // under that length fails; without a limit, it returns the length.
    public static TheoryData<string> Builders => new()
    {
        "let $v := string-join((1 to 100) ! 'x') return string-length(concat($v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v, $v))",
        "string-length(string-join((1 to 2000) ! 'x'))",
        "string-length(string-join((1 to 1000) ! 'x', ','))",
        "let $v := string-join((1 to 100) ! 'x') return string-length($v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v || $v)",
        "string-length(``[`{(1 to 1000) ! 'x'}`]``)",
        "string-length(codepoints-to-string((1 to 2000) ! 120))",
        "string-length(replace(string-join((1 to 100) ! 'x'), 'x', 'yyyyyyyyyyyyyyyyyyyy'))",
        "string-length(encode-for-uri(string-join((1 to 700) ! ' ')))",
        "string-length(serialize(<a>{(1 to 2000) ! 'x'}</a>))",
        "<a>{(1 to 2000) ! text { 'x' }}</a>",
        "<a>{(1 to 2000) ! 'x'}</a>",
        "document { (1 to 2000) ! text { 'x' } }",
        "string-length(string(attribute a { (1 to 1000) ! 'x' }))",
        "text { (1 to 1000) ! 'x' }",
    };

    [Theory]
    [MemberData(nameof(Builders))]
    public async Task A_string_over_the_limit_is_FOER0000(string query)
        => (await RunAsync(query, Limit)).Should().StartWith("FOER0000").And.Contain("MaxStringLength");

    [Theory]
    [MemberData(nameof(Builders))]
    public async Task Without_a_limit_the_same_query_runs(string query)
        => (await RunAsync(query, null)).Should().NotStartWith("FOER0000");

    [Theory]
    [InlineData("string-length(string-join((1 to 900) ! 'x'))", "900")]
    [InlineData("string-length(concat(string-join((1 to 500) ! 'x'), string-join((1 to 500) ! 'y')))", "1000")]
    [InlineData("string-length(replace(string-join((1 to 100) ! 'x'), 'x', 'yy'))", "200")]
    public async Task A_string_at_or_under_the_limit_is_built(string query, string expected)
        => (await RunAsync(query, Limit)).Should().Be(expected);

    /// <summary>A query can catch the error, and the evaluation goes on.</summary>
    [Fact]
    public async Task The_error_can_be_caught_in_the_query()
        => (await RunAsync("try { string-join((1 to 5000) ! 'x') } catch err:FOER0000 { 'caught' }", Limit))
            .Should().Be("caught");

    [Fact]
    public void CheckStringLength_is_available_to_hosts()
    {
        var context = new QueryEngine().CreateContext(limits: new QueryExecutionLimits { MaxStringLength = 10 });
        context.Invoking(c => c.CheckStringLength(10)).Should().NotThrow();
        context.Invoking(c => c.CheckStringLength(11)).Should().Throw<XQueryRuntimeException>()
            .Which.ErrorCode.Should().Be("FOER0000");
    }
}

/// <summary>
/// The allocation half of <see cref="MaxStringLengthTests"/>. The allocation counter is process-wide,
/// so these run alone, in the timing-sensitive collection, with no neighbour allocating meanwhile.
/// </summary>
[Collection(TimingSensitiveTests.Name)]
public sealed class MaxStringLengthAllocationTests
{
    private static Task<string> RunAsync(string query, int? maxStringLength)
        => MaxStringLengthTests.RunAsync(query, maxStringLength);

    /// <summary>
    /// fn:string-join stops when the result would pass the limit. It does not build the whole
    /// string first: 20,000,000 characters are 40 MB, and the join allocates far less.
    /// </summary>
    [Fact]
    public async Task String_join_stops_before_it_builds_the_whole_string()
    {
        var query = "let $v := string-join((1 to 10000) ! 'x') return string-join((1 to 2000) ! $v)";
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var outcome = await RunAsync(query, 100_000);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        outcome.Should().StartWith("FOER0000");
        allocated.Should().BeLessThan(10_000_000);
    }

    /// <summary>fn:replace stops at the limit, not after it has replaced every match.</summary>
    [Fact]
    public async Task Replace_stops_before_it_builds_the_whole_result()
    {
        // Without the limit the result is 10,000 × 2,000 = 20,000,000 characters (40 MB).
        var query = "replace(string-join((1 to 10000) ! 'x'), 'x', '" + new string('y', 2000) + "')";
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var outcome = await RunAsync(query, 100_000);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        outcome.Should().StartWith("FOER0000");
        allocated.Should().BeLessThan(10_000_000);
    }
}
