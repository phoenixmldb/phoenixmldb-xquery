using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Extended node store with node creation capabilities.
/// Adds ID allocation, node registration, and namespace interning for functions
/// that construct new XDM trees (e.g., <c>fn:parse-xml</c>, <c>fn:json-to-xml</c>).
/// </summary>
public interface INodeBuilder : INodeStore
{
    /// <summary>
    /// Allocates a new unique <see cref="NodeId"/>.
    /// </summary>
    NodeId AllocateId();

    /// <summary>
    /// Allocates a new unique <see cref="DocumentId"/> for a tree the caller builds and
    /// registers itself (the annotating parse of a validated document). Documents sharing an id
    /// are indistinguishable to a lookup by document, so each tree needs its own. The default
    /// derives one from a fresh node id, tagged with the high bit so it cannot meet an id a
    /// store numbers from 1.
    /// </summary>
    DocumentId AllocateDocumentId() => new(AllocateId().Value | (1UL << 63));

    /// <summary>
    /// Marks every node id up to and including <paramref name="last"/> as used, after a parser
    /// has numbered a tree's nodes itself from one allocated start id. Without it the next
    /// allocation lands inside that tree and its nodes overwrite the first tree's. The default
    /// allocates until past <paramref name="last"/>, which suits a monotonic allocator.
    /// </summary>
    void ReserveIdsThrough(NodeId last)
    {
        while (AllocateId().Value < last.Value) { }
    }

    /// <summary>
    /// Registers a node in the store, making it resolvable via <see cref="INodeProvider.GetNode"/>.
    /// </summary>
    /// <param name="node">The node to register.</param>
    void RegisterNode(XdmNode node);

    /// <summary>
    /// Interns a namespace URI, returning a stable <see cref="NamespaceId"/> for it.
    /// If the URI has already been interned, returns the existing ID.
    /// </summary>
    /// <param name="uri">The namespace URI to intern.</param>
    /// <returns>The interned namespace identifier.</returns>
    NamespaceId InternNamespace(string uri);

    /// <summary>
    /// Interns a namespace URI with a hint at the desired <see cref="NamespaceId"/>.
    /// If the URI is already interned, returns the existing ID. Otherwise, the store
    /// may honor <paramref name="preferredId"/> (typically allocated by static analysis
    /// before the runtime store was instantiated) so that pre-assigned IDs round-trip
    /// through serialization. If <paramref name="preferredId"/> is <see cref="NamespaceId.None"/>
    /// or unusable, the store allocates a fresh ID.
    /// </summary>
    /// <param name="uri">The namespace URI to intern.</param>
    /// <param name="preferredId">A pre-allocated ID the store may adopt for this URI.</param>
    /// <returns>The ID actually used by the store — callers must use this value, not <paramref name="preferredId"/>.</returns>
    NamespaceId InternNamespace(string uri, NamespaceId preferredId)
        => InternNamespace(uri);
}
