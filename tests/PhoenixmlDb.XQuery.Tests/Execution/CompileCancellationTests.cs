using System.Diagnostics;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// <see cref="QueryEngine.Compile(string, CompilationOptions?, CancellationToken)"/> stops when its
/// token is cancelled: the parser, the static analysis and the optimizer check it as they walk the
/// query. A host that compiles queries it was sent held a worker for the whole compile, with no way
/// to give up on one.
/// </summary>
[Collection(TimingSensitiveTests.Name)]
public sealed class CompileCancellationTests
{
    // 50,000 calls: about a second to compile, so a cancellation after 20 ms falls part-way.
    private static readonly string LargeQuery = "(" + string.Join(", ", Enumerable.Repeat("abs(1) + 1", 50_000)) + ")";

    [Fact]
    public void A_cancelled_token_stops_the_compile_before_it_starts()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var act = () => new QueryEngine().Compile("1 + 1", null, cancelled.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void A_compile_stops_part_way_when_the_token_is_cancelled()
    {
        var engine = new QueryEngine();
        var whole = Stopwatch.StartNew();
        engine.Compile(LargeQuery).Success.Should().BeTrue();
        whole.Stop();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var stopped = Stopwatch.StartNew();
        var act = () => engine.Compile(LargeQuery, null, cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
        stopped.Stop();
        stopped.Elapsed.Should().BeLessThan(whole.Elapsed,
            $"the whole compile took {whole.ElapsedMilliseconds} ms; it was cancelled after 20 ms");
    }

    [Fact]
    public void The_analysis_of_a_parsed_query_stops_when_the_token_is_cancelled()
    {
        var expression = new XQueryParserFacade().Parse(LargeQuery);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        var act = () => new QueryEngine().Compile(expression, null, cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void A_token_that_is_not_cancelled_changes_nothing()
    {
        using var cancellation = new CancellationTokenSource();

        var result = new QueryEngine().Compile("for $i in 1 to 3 return $i * 2", null, cancellation.Token);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_passes_its_token_to_the_compile()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var engine = new QueryEngine();

        var act = async () =>
        {
            await foreach (var _ in engine.ExecuteAsync(LargeQuery, cancellationToken: cancellation.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
