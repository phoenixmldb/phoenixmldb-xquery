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

    /// <summary>
    /// The results of an operand, for an operator to enumerate. A chain of operators (`$a + $b +
    /// ...`, `a/b/c/...`, `$x[1][1]...`) is an operator tree one level deeper per link, and its
    /// asynchronous enumeration takes stack in proportion. Where the stack runs low, an operand
    /// that evaluates synchronously continues on a new stack; any other fails with XPDY0130, which
    /// a caller can catch, instead of overflowing the stack, which ends the process.
    /// </summary>
    internal static IAsyncEnumerable<object?> ExecuteGuarded(this PhysicalOperator op, QueryExecutionContext context)
    {
        if (HasRoom())
            return op.ExecuteAsync(context);
        if (OnNewStack(() => PhysicalOperator.CanEvaluateSync(op)))
            return ItemsOf(OnNewStack(() => op.EvaluateSync(context)));
        throw TooDeep();
    }

    /// <summary>
    /// For an asynchronous recursion other than an operand's (one level per FLWOR clause, say):
    /// fails with XPDY0130 where the stack runs low.
    /// </summary>
    internal static void EnsureRoom()
    {
        if (!HasRoom())
            throw TooDeep();
    }

    private static XQueryRuntimeException TooDeep() => new("XPDY0130",
        "The expression is nested too deeply to evaluate on this thread's stack. " +
        "Run the query on a thread with a larger stack, or nest it less deeply.");

    /// <summary>The synchronous evaluation of an operand, on a new stack where the stack runs low.</summary>
    internal static object? EvaluateSyncGuarded(this PhysicalOperator op, QueryExecutionContext context)
        => HasRoom() ? op.EvaluateSync(context) : OnNewStack(() => op.EvaluateSync(context));

    /// <summary>Whether an operand supports synchronous evaluation, on a new stack where the stack runs low.</summary>
    internal static bool SupportsSyncGuarded(this PhysicalOperator op)
        => HasRoom() ? op.SupportsSync : OnNewStack(() => op.SupportsSync);

    private static async IAsyncEnumerable<object?> ItemsOf(object? result)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        if (result is object?[] items)
        {
            foreach (var item in items)
                yield return item;
        }
        else if (result != null)
        {
            yield return result;
        }
    }
}
