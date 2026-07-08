using System.Threading;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Allocates process-global, monotonic tree ordinals stamped onto <c>XdmNode.TreeOrdinal</c>
/// so that XDM document order — the pair <c>(TreeOrdinal, Id)</c> — is total and consistent
/// across nodes drawn from independently-constructed node stores (issue #188).
/// </summary>
/// <remarks>
/// <para>
/// A single static counter feeds every store in the process, so two independently-parsed
/// <see cref="XdmDocumentStore"/> instances receive disjoint ordinals and their nodes never
/// interleave by the (per-store, non-unique) <c>NodeId</c>.
/// </para>
/// <para>
/// Parse-time ordinals occupy the low range (high bit clear); construction-time ordinals set the
/// high bit (<see cref="ConstructedFlag"/>). Because any construction ordinal therefore exceeds
/// any parse ordinal, constructed trees always sort <em>after</em> parsed documents regardless of
/// the order in which they are allocated within a query — preserving the legacy single-store rule
/// that constructed nodes sort last.
/// </para>
/// <para>
/// Within a single store this is order-preserving: documents are stamped with ascending ordinals
/// in load order (matching their ascending NodeId blocks), and all of a store's constructed nodes
/// share one construction epoch (tie-broken by NodeId), so <c>(TreeOrdinal, Id)</c> reduces to the
/// pre-existing NodeId order. See <c>DocumentOrderTests</c> / <c>DocumentOrderBug188Tests</c>.
/// </para>
/// </remarks>
internal static class TreeOrdinalAllocator
{
    /// <summary>High bit marking a construction-time ordinal; guarantees it outranks any parse ordinal.</summary>
    internal const ulong ConstructedFlag = 1UL << 63;

    private static ulong s_next;

    /// <summary>Allocates the next parse-time ordinal (low range, high bit clear).</summary>
    internal static ulong NextParseOrdinal() => Interlocked.Increment(ref s_next);

    /// <summary>Allocates the next construction-time ordinal (high range, high bit set).</summary>
    internal static ulong NextConstructionOrdinal() => Interlocked.Increment(ref s_next) | ConstructedFlag;
}
