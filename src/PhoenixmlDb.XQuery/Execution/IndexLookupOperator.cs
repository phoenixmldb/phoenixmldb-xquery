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

    /// <summary>The lookup key (typically the attribute value).</summary>
    public required object Key { get; init; }

    /// <summary>
    /// Runtime callback that dispatches to the indexing layer. Returns the
    /// matching items (typically <c>XdmNode</c> instances). When unset, the
    /// operator yields nothing.
    /// </summary>
    public Func<string, object, QueryExecutionContext, IAsyncEnumerable<object?>>? LookupAsync { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        if (LookupAsync == null)
        {
            await Task.CompletedTask;
            yield break;
        }

        await foreach (var item in LookupAsync(IndexName, Key, context))
            yield return item;
    }
}
