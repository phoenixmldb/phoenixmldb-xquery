using System.Runtime.CompilerServices;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Browser WebAssembly and WASI cannot start a thread. Where the stack ran low there, the guards
/// started one to continue on, and Thread.Start threw PlatformNotSupportedException: a memoised
/// recursive XSLT function that 2.9.0 ran in the browser failed. Without threads the guards must
/// continue on the current stack, as before them. <see cref="StackGuard.ForceNoThreads"/>
/// simulates such a platform; the checks run where the runtime's own stack check fails, which is
/// where the guards act.
/// </summary>
public class StackGuardWithoutThreadsTests
{
    [Fact]
    public void Where_the_stack_runs_low_and_no_thread_can_start_the_guards_continue_on_the_current_stack()
    {
        Outcome outcome = default;
        var thread = new Thread(() =>
        {
            StackGuard.ForceNoThreads.Value = true;
            outcome = AtLowStack(0);
        }, 1024 * 1024);

        thread.Start();
        thread.Join();

        outcome.Failure.Should().BeNull();
        outcome.Levels.Should().BePositive();
        outcome.RuntimeHasRoom.Should().BeFalse("the checks must run where the runtime's stack check fails");
        outcome.GuardHasRoom.Should().BeTrue();
        outcome.CallThread.Should().Be(outcome.CallerThread, "no thread can be started");
        outcome.Items.Should().Equal(42);
    }

    [Fact]
    public void Where_threads_can_start_a_low_stack_still_moves_to_a_new_thread()
    {
        Outcome outcome = default;
        var thread = new Thread(() => outcome = AtLowStack(0), 1024 * 1024);

        thread.Start();
        thread.Join();

        outcome.Failure.Should().BeOfType<XQueryRuntimeException>().Which.ErrorCode.Should().Be("XPDY0130");
        outcome.GuardHasRoom.Should().BeFalse();
        outcome.CallThread.Should().NotBe(outcome.CallerThread);
        outcome.Items.Should().Equal(42);
    }

    private record struct Outcome(
        int Levels, bool RuntimeHasRoom, bool GuardHasRoom, int CallerThread, int CallThread,
        List<object?> Items, Exception? Failure);

    // Recurses until the runtime's stack check fails, then asks the guards what they do there.
    // Only values are taken at the bottom; they are examined once the stack has unwound.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Outcome AtLowStack(int levels)
    {
        if (RuntimeHelpers.TryEnsureSufficientExecutionStack())
            return AtLowStack(levels + 1);

        var outcome = new Outcome(
            levels,
            RuntimeHelpers.TryEnsureSufficientExecutionStack(),
            StackGuard.HasRoom(),
            Environment.CurrentManagedThreadId,
            StackGuard.OnNewStack(() => Environment.CurrentManagedThreadId),
            [],
            null);

        var items = StackGuard.ExecuteGuarded(new ConstantOperator { Value = 42 }, null!).GetAsyncEnumerator();
        while (items.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            outcome.Items.Add(items.Current);

        try { StackGuard.EnsureRoom(); }
#pragma warning disable CA1031 // examined by the test
        catch (Exception e) { outcome.Failure = e; }
#pragma warning restore CA1031
        return outcome;
    }
}
