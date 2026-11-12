using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>The timeout helper the cancellation tests rely on reports both outcomes.</summary>
public sealed class TaskTimeoutTests
{
    [Fact]
    public async Task A_task_that_never_finishes_is_reported_as_not_finished()
    {
        var never = new TaskCompletionSource().Task;
        (await TaskTimeout.FinishesWithinAsync(never, TimeSpan.FromMilliseconds(50))).Should().BeFalse();
    }

    [Fact]
    public async Task A_completed_task_is_reported_as_finished()
        => (await TaskTimeout.FinishesWithinAsync(Task.CompletedTask, TimeSpan.FromSeconds(5))).Should().BeTrue();

    [Fact]
    public async Task A_failed_task_is_reported_as_finished()
        => (await TaskTimeout.FinishesWithinAsync(Task.FromException(new InvalidOperationException()), TimeSpan.FromSeconds(5)))
            .Should().BeTrue();
}
