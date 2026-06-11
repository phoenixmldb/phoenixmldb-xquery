namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Physical operator that produces nodes by consulting an indexer rather than
/// scanning the input. The optimizer emits this when an index covers the path
/// shape. Runtime dispatch is delegated via <see cref="LookupAsync"/> — the
/// adapter in <c>PhoenixmlDb.Indexing</c> wires the actual indexer invocation.
/// </summary>
public sealed class IndexLookupOperator : PhysicalOperator
{
    /// <summary>The index name returned by <see cref="Optimizer.IndexCoverage.IndexName"/>.</summary>
    public required string IndexName { get; init; }

    /// <summary>The predicate to resolve against the index (equality or range).</summary>
    public required IndexPredicate Predicate { get; init; }

    /// <summary>
    /// Runtime callback that dispatches to the indexing layer. Returns the
    /// matching items (typically <c>XdmNode</c> instances). When unset, the
    /// operator yields nothing. The <c>object</c> argument is the <see cref="Predicate"/>.
    /// </summary>
    public Func<string, object, QueryExecutionContext, IAsyncEnumerable<object?>>? LookupAsync { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Inline delegate wins — keeps unit tests free to wire a mock without
        // standing up an IIndexLookupResolver. Production callers attach a
        // resolver to the context; tests that just inspect plan shape neither.
        var stream = LookupAsync != null
            ? LookupAsync(IndexName, Predicate, context)
            : context.IndexLookupResolver?.ResolveAsync(IndexName, Predicate, context);

        if (stream == null)
        {
            await Task.CompletedTask;
            yield break;
        }

        await foreach (var item in stream)
            yield return item;
    }
}
