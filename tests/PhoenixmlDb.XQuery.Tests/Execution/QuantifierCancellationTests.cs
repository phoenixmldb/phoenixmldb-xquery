using System.Threading;
using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// QuantifiedOperator's <c>some</c> / <c>every</c> foreach loops must poll the
/// execution context's cancellation token so external timeouts are honoured.
/// Without this, QT3 same-key-023 (every-loop over 421,875 keys, each doing
/// O(N) map:remove + map:put work) ran for hours past the 15s per-test
/// cancellation token, silently killing the entire conformance sweep.
/// </summary>
public sealed class QuantifierCancellationTests
{
    /// <summary>
    /// A source that is cancelled 200 ms from now by a thread of its own. A source made with a
    /// delay is cancelled by a timer, whose callback needs a thread-pool thread: on a busy
    /// machine it came later than the loop under test ended, and the test saw no cancellation
    /// from a loop that polls correctly (Windows CI, net10.0).
    /// </summary>
    private static CancellationTokenSource CancelAfter200Milliseconds()
    {
        var source = new CancellationTokenSource();
        new Thread(() =>
        {
            Thread.Sleep(200);
            try { source.Cancel(); } catch (ObjectDisposedException) { }
        }) { IsBackground = true }.Start();
        return source;
    }

    [Fact]
    public async System.Threading.Tasks.Task EveryQuantifier_HonoursCancellationOnLongLoop()
    {
        var query = """
            let $keys := 1 to 2000000
            return every $k in $keys satisfies $k > 0
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue();

        using var cts = CancelAfter200Milliseconds();
        using var ctx = engine.CreateContext(cancellationToken: cts.Token);

        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async System.Threading.Tasks.Task FlworFor_HonoursCancellationOnLargeCartesianProduct()
    {
        // QT3 XMark-Q8 generates a 600×600 person/closed_auction cartesian product
        // and the testhost hung at 0% CPU for over an hour past the per-test
        // timeout because FlworOperator's main per-tuple loop never polled
        // cancellation. 2M×2M here is overkill but cheap to set up and proves
        // the per-tuple poll fires within the cancellation window.
        var query = """
            let $xs := 1 to 2000
            let $ys := 1 to 2000
            for $x in $xs, $y in $ys
            return $x + $y
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue();

        using var cts = CancelAfter200Milliseconds();
        using var ctx = engine.CreateContext(cancellationToken: cts.Token);

        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async System.Threading.Tasks.Task SomeQuantifier_HonoursCancellationOnLongLoop()
    {
        var query = """
            let $keys := 1 to 2000000
            return some $k in $keys satisfies $k = -1
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue();

        using var cts = CancelAfter200Milliseconds();
        using var ctx = engine.CreateContext(cancellationToken: cts.Token);

        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
