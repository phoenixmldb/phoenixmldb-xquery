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

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
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

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        using var ctx = engine.CreateContext(cancellationToken: cts.Token);

        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
