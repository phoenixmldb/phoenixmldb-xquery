using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A failure deep in a chain of operands reaches the caller with a short stack trace. Each async
/// frame that a failure leaves copied the whole trace on the way out, so the time to return it
/// grew with the square of the depth: on a 16 MB thread a path of 6,000 steps failed after 14 s
/// with a trace of about 21,000 lines, and on a 64 MB thread one of 21,000 steps after more than
/// three minutes. The trace is now dropped every few levels: under 100 lines and about 0.1 s.
/// The length of the trace is measured, not the time, so the test does not depend on the runner.
/// The test chain is longer, so the test does not depend on the JIT tier.
/// </summary>
public sealed class DeepFailureUnwindTests
{
    [Fact]
    public void A_failure_at_the_end_of_the_stack_returns_with_a_short_trace()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var store = new XdmDocumentStore();
            var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
            var compiled = engine.Compile("count(parse-xml('<a/>') ! (." + string.Concat(Enumerable.Repeat("//a", 30_000)) + "))");
            try
            {
#pragma warning disable xUnit1031, CA1849 // the point is to stay on this thread's stack
                var items = compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()).GetAsyncEnumerator();
                while (items.MoveNextAsync().AsTask().GetAwaiter().GetResult()) { }
#pragma warning restore xUnit1031, CA1849
            }
#pragma warning disable CA1031 // reported through the assertions below
            catch (Exception ex) { failure = ex; }
#pragma warning restore CA1031
        }, maxStackSize: 16 * 1024 * 1024);
        thread.Start();
        thread.Join();

        failure.Should().BeOfType<XQueryRuntimeException>()
            .Which.ErrorCode.Should().Be("XPDY0130");
        (failure!.StackTrace ?? "").Split('\n').Length.Should().BeLessThan(500,
            "a trace that spans every level costs time with the square of the depth to return");
    }
}
