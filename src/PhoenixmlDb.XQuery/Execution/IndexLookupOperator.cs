using PhoenixmlDb.Core;

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
        // A predicate slot may carry a VariableComparand sentinel (when the
        // optimizer recognized `@attr = $var`) instead of a plan-time literal.
        // Resolve it to the variable's bound CLR value now, before dispatch, so
        // the indexing layer sees the same literal shape a `@attr = "lit"` query
        // would have produced. A pure-literal predicate reconstructs identically.
        var resolved = ResolvePredicate(Predicate, context);

        // Inline delegate wins — keeps unit tests free to wire a mock without
        // standing up an IIndexLookupResolver. Production callers attach a
        // resolver to the context; tests that just inspect plan shape neither.
        var stream = LookupAsync != null
            ? LookupAsync(IndexName, resolved, context)
            : context.IndexLookupResolver?.ResolveAsync(IndexName, resolved, context);

        if (stream == null)
        {
            await Task.CompletedTask;
            yield break;
        }

        await foreach (var item in stream)
            yield return item;
    }

    /// <summary>
    /// Reconstructs <paramref name="predicate"/> with any <see cref="VariableComparand"/>
    /// slot replaced by the variable's resolved CLR value. A predicate whose slots are
    /// all literals is reconstructed identically, so the literal path is unaffected.
    /// </summary>
    private static IndexPredicate ResolvePredicate(IndexPredicate predicate, QueryExecutionContext context) => predicate switch
    {
        IndexEquality eq => new IndexEquality(ResolveSlot(eq.Value, context)!),
        IndexRange r => new IndexRange(
            ResolveSlot(r.Lower, context), r.LowerInclusive,
            ResolveSlot(r.Upper, context), r.UpperInclusive),
        _ => predicate,
    };

    /// <summary>
    /// Passes a literal slot through untouched; resolves a <see cref="VariableComparand"/>
    /// to the variable's bound CLR value (throwing if unbound — never a silent null).
    /// </summary>
    private static object? ResolveSlot(object? slot, QueryExecutionContext context)
    {
        if (slot is not VariableComparand v) return slot;
        return ResolveVariableToClr(v.Name, context);
    }

    /// <summary>
    /// Resolves a variable to the boxed CLR comparand the indexing layer expects — the
    /// same shape (<c>string</c> / <c>long</c>-or-<c>BigInteger</c> / <c>decimal</c> /
    /// <c>double</c> / <c>bool</c>) a plan-time literal would have produced. Mirrors
    /// <see cref="VariableOperator"/>'s scope lookup, with the external-variable fallback
    /// for bindings supplied before the matching <c>declare variable … external</c> runs.
    /// Throws <see cref="XQueryRuntimeException"/> (XPST0008) if the variable is unbound —
    /// returning null would silently yield empty results and mask the error.
    /// </summary>
    private static object? ResolveVariableToClr(QName name, QueryExecutionContext context)
    {
        if (!context.TryGetVariable(name, out var value)
            && !context.TryGetExternalVariable(name, out value))
        {
            throw new XQueryRuntimeException("XPST0008", $"Variable ${name} not bound");
        }
        return UnwrapAtomic(value);
    }

    /// <summary>
    /// Unwraps an atomic XDM wrapper to its raw CLR value so the comparand matches the
    /// shape a literal produced (e.g. <c>XsUntypedAtomic("Acme")</c> -> <c>"Acme"</c>).
    /// Raw CLR values and anything else pass through unchanged.
    /// </summary>
    private static object? UnwrapAtomic(object? value) => value switch
    {
        Xdm.XsUntypedAtomic ua => ua.Value,
        Xdm.XsAnyUri uri => uri.Value,
        Xdm.XsTypedString ts => ts.Value,
        Xdm.XsTypedInteger ti => ti.Value,
        _ => value,
    };
}
