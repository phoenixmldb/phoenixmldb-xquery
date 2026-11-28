using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// The namespace URIs and the nodes of the ids of a node. The ids are the node's store's. One rule
/// for each method: for a node that a store holds (<see cref="XdmNode.Store"/>), the answer comes
/// from that store only, and an id that store does not know has no URI and no node. Another
/// store's ids can name other URIs and other nodes, and a wrong answer is worse than none. Only a
/// node that no store holds is read in the context's stores: the node provider when it is an
/// <see cref="INodeStore"/>, the context's node store, then the context's namespace resolver.
/// Reading only the node provider gave a wrong or no URI for a node of another store, and none at
/// all for a host whose node provider is not an <see cref="INodeStore"/> (a
/// <see cref="DelegateNodeProvider"/>).
/// </summary>
internal static class NodeNamespaces
{
    /// <summary>
    /// The URI of <paramref name="id"/> of <paramref name="node"/>: from the node's own store, or,
    /// for a node no store holds, from the fallbacks of <paramref name="context"/>.
    /// </summary>
    internal static string? UriOf(XdmNode? node, NamespaceId id, Ast.ExecutionContext? context)
    {
        if (id == NamespaceId.None)
            return "";
        if (node?.Store is { } own)
            return own.GetNamespaceUri(id);
        var query = context as QueryExecutionContext;
        if (query?.NodeProvider is INodeStore provider && provider.GetNamespaceUri(id) is { } fromProvider)
            return fromProvider;
        if (context?.NodeStore is { } store && store.GetNamespaceUri(id) is { } fromStore)
            return fromStore;
        return query?.NamespaceResolver?.Invoke(id);
    }

    /// <summary>
    /// The URI of <paramref name="id"/> of <paramref name="node"/>: from the node's own store, or,
    /// for a node no store holds, from <paramref name="provider"/>.
    /// </summary>
    internal static string? UriOf(XdmNode? node, NamespaceId id, INodeProvider? provider)
    {
        if (id == NamespaceId.None)
            return "";
        if (node?.Store is { } own)
            return own.GetNamespaceUri(id);
        return (provider as INodeStore)?.GetNamespaceUri(id);
    }

    /// <summary>
    /// The node of <paramref name="id"/> in the tree of <paramref name="node"/>: from the node's own
    /// store, or, for a node no store holds, from <paramref name="provider"/>.
    /// </summary>
    internal static XdmNode? NodeOf(XdmNode node, NodeId id, INodeProvider? provider)
        => node.Store is { } own ? own.GetNode(id) : provider?.GetNode(id);

    /// <summary>
    /// The store to read the tree of <paramref name="node"/> in: the node's own store (as an
    /// <see cref="INodeStore"/>, or adapted to one when it is only an <see cref="IXdmNodeStore"/>,
    /// as an XSLT temporary tree's is), or, for a node no store holds, <paramref name="fallback"/>
    /// (the context's).
    /// </summary>
    internal static INodeStore? StoreOf(XdmNode? node, INodeStore? fallback) => node?.Store switch
    {
        null => fallback,
        INodeStore store => store,
        { } own => new NodeStoreAdapter(new DelegateNodeProvider(own.GetNode), own.GetNamespaceUri),
    };
}
