namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Dispatches a runtime index lookup to the storage layer. Implementations
/// live in the indexing project (e.g. PhoenixmlDb.Indexing) — the XQuery layer
/// only knows the interface so plans stay independent of the storage tier.
/// </summary>
/// <remarks>
/// The optimizer emits <see cref="IndexLookupOperator"/> nodes carrying the
/// <c>IndexName</c> returned by <see cref="Optimizer.IndexCoverage"/> and the
/// literal predicate value as <c>Key</c>. At execute time, when the operator's
/// inline <c>LookupAsync</c> delegate is null, it falls back to the resolver
/// attached to the active <see cref="QueryExecutionContext"/>. The contract is
/// "give me the matching items for this index/key" — typically <c>XdmNode</c>
/// instances, but the operator yields <c>object?</c> to mirror the rest of the
/// physical-operator surface.
/// </remarks>
public interface IIndexLookupResolver
{
    /// <summary>
    /// Streams items matching the indexed predicate. Returning an empty
    /// sequence is a valid "no match" — exceptions are reserved for genuine
    /// errors (unknown index name, malformed key, storage failure).
    /// </summary>
    IAsyncEnumerable<object?> ResolveAsync(string indexName, object key, QueryExecutionContext context);
}
