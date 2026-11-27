using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// The namespace URI of a namespace id of a node. The id is the node's store's: the URI comes from
/// the node's own store (<see cref="XdmNode.Store"/>) first. A node that no store holds falls back
/// to the context's node provider when it is an <see cref="INodeStore"/>, then to the context's
/// namespace resolver. Reading only the node provider gave a wrong or no URI for a node of another
/// store, and none at all for a host whose node provider is not an <see cref="INodeStore"/> (a
/// <see cref="DelegateNodeProvider"/>).
/// </summary>
internal static class NodeNamespaces
{
    /// <summary>The URI of <paramref name="id"/> of <paramref name="node"/>, with the fallbacks of <paramref name="context"/>.</summary>
    internal static string? UriOf(XdmNode? node, NamespaceId id, Ast.ExecutionContext? context)
    {
        if (id == NamespaceId.None)
            return "";
        if (node?.Store is { } own && own.GetNamespaceUri(id) is { } uri)
            return uri;
        var query = context as QueryExecutionContext;
        if (query?.NodeProvider is INodeStore provider && provider.GetNamespaceUri(id) is { } fromProvider)
            return fromProvider;
        if (context?.NodeStore is { } store && store.GetNamespaceUri(id) is { } fromStore)
            return fromStore;
        return query?.NamespaceResolver?.Invoke(id);
    }

    /// <summary>The URI of <paramref name="id"/> of <paramref name="node"/>, with <paramref name="provider"/> as the fallback.</summary>
    internal static string? UriOf(XdmNode? node, NamespaceId id, INodeProvider? provider)
    {
        if (id == NamespaceId.None)
            return "";
        if (node?.Store is { } own && own.GetNamespaceUri(id) is { } uri)
            return uri;
        return (provider as INodeStore)?.GetNamespaceUri(id);
    }

    /// <summary>
    /// The node of <paramref name="id"/> in the tree of <paramref name="node"/>: from the node's own
    /// store first, then from <paramref name="provider"/>.
    /// </summary>
    internal static XdmNode? NodeOf(XdmNode node, NodeId id, INodeProvider? provider)
        => node.Store?.GetNode(id) ?? provider?.GetNode(id);

    /// <summary>
    /// The store to read the tree of <paramref name="node"/> in: the node's own store when it is an
    /// <see cref="INodeStore"/>, else <paramref name="fallback"/> (the context's).
    /// </summary>
    internal static INodeStore? StoreOf(XdmNode? node, INodeStore? fallback)
        => node?.Store as INodeStore ?? fallback;
}
