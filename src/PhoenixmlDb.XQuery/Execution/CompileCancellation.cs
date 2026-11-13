namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// The cancellation token of the compilation in progress, for the parser, the static analysis and
/// the optimizer to check as they walk the query. It is ambient rather than passed down because
/// those passes are many visitors over the same tree, with one shared entry for each node
/// (<see cref="Ast.XQueryExpressionWalker.Walk"/>, <see cref="Ast.XQueryExpressionRewriter.Rewrite"/>,
/// the parser's rule listener, the physical planner). An <see cref="AsyncLocal{T}"/>, not a
/// thread-static field: a deep tree continues on a new thread (<see cref="StackGuard.OnNewStack{T}"/>),
/// and the execution context, with this value, flows to that thread.
/// </summary>
internal static class CompileCancellation
{
    private static readonly AsyncLocal<CancellationToken> s_token = new();

    /// <summary>Throws <see cref="OperationCanceledException"/> when the compilation in progress was cancelled.</summary>
    internal static void ThrowIfRequested() => s_token.Value.ThrowIfCancellationRequested();

    /// <summary>Makes <paramref name="token"/> the compilation's token until the result is disposed.</summary>
    internal static Scope Use(CancellationToken token)
    {
        var before = s_token.Value;
        s_token.Value = token;
        return new Scope(before);
    }

    internal readonly struct Scope(CancellationToken before) : IDisposable
    {
        public void Dispose() => s_token.Value = before;
    }
}
