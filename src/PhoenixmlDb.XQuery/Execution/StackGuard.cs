using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Continues a recursion over a tree on a fresh thread stack when the current one runs low. A
/// method that recurses once per level of a tree (serializing it, comparing it) overflowed the
/// stack on a deeply nested tree, which ends the process: a .NET StackOverflowException cannot
/// be caught. Such a method asks <see cref="HasRoom"/> at its entry and, when there is not, runs
/// the rest of its call through <see cref="OnNewStack{T}"/>, on a thread with a 16 MB stack that
/// the caller waits for. Each hop gives that much more room, so the depth is bounded by memory,
/// not by the first thread's stack (1 MB by default on Windows).
/// </summary>
internal static class StackGuard
{
    private const int StackSize = 16 * 1024 * 1024;

    /// <summary>Whether the current stack has the room the runtime asks for to continue safely.</summary>
    internal static bool HasRoom() => RuntimeHelpers.TryEnsureSufficientExecutionStack();

    /// <summary>Runs <paramref name="call"/> on a new thread with a large stack, and returns its result.</summary>
    internal static T OnNewStack<T>(Func<T> call)
    {
        T result = default!;
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
#pragma warning disable CA1031 // rethrown on the calling thread, as it was
            try { result = call(); }
            catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
#pragma warning restore CA1031
        }, StackSize);
        thread.Start();
        thread.Join();
        error?.Throw();
        return result;
    }

    /// <summary>Runs <paramref name="call"/> on a new thread with a large stack.</summary>
    internal static void OnNewStack(Action call)
        => OnNewStack<object?>(() => { call(); return null; });
}
