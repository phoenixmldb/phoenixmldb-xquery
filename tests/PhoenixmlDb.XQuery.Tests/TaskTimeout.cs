namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Whether a task finishes within a time limit. <c>Task.WhenAny(task, Task.Delay(limit))</c> left the
/// delay's timer running after the task finished, which analyzer rule CA2027 reports as an error.
/// </summary>
internal static class TaskTimeout
{
    /// <summary>
    /// True when <paramref name="task"/> completes (succeeds, fails or is cancelled) within
    /// <paramref name="limit"/>; false when the limit passes first. The task's own outcome is
    /// left for the caller to await.
    /// </summary>
    internal static async Task<bool> FinishesWithinAsync(Task task, TimeSpan limit)
    {
        var waited = task.WaitAsync(limit);
        await waited.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return !(waited.IsFaulted && waited.Exception?.InnerException is TimeoutException);
    }
}
