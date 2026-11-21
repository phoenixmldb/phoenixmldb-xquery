using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Provides node resolution for the XQuery execution engine.
/// </summary>
/// <remarks>
/// <para>
/// This is an extension point that allows the XQuery engine to load XDM nodes from any storage backend.
/// Implementations can load nodes from PhoenixmlDb's built-in storage, in-memory documents,
/// a remote service, or any other source.
/// </para>
/// <para>
/// Most developers do not need to implement this interface directly — the built-in PhoenixmlDb storage
/// layer provides its own implementation. Implement it when integrating the XQuery engine with a
/// custom data source or when building a virtual/lazy-loading node store.
/// </para>
/// <para>
/// A provider should also implement <see cref="IXdmNodeStore"/> and attach each node it returns to
/// itself (<see cref="XdmNode.AttachStore"/>). Node ids and namespace ids mean something only in the
/// store that assigned them; a node that names its store is then read in that store wherever it is
/// passed, and a node of another store that names none is refused (phx:XPDY9002) instead of being
/// read in the wrong store. A provider that does neither works as before 3.3.0, but the engine
/// cannot tell its nodes from another store's.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Simple in-memory node provider:
/// var provider = new DelegateNodeProvider(id => nodeCache.TryGetValue(id, out var node) ? node : null);
/// var engine = new QueryEngine(nodeProvider: provider);
/// </code>
/// </example>
/// <seealso cref="DelegateNodeProvider"/>
/// <seealso cref="Execution.QueryEngine"/>
public interface INodeProvider
{
    /// <summary>
    /// Loads a node by its storage identifier.
    /// </summary>
    /// <param name="nodeId">The identifier of the node to load.</param>
    /// <returns>The XDM node, or <c>null</c> if no node exists with the given identifier.</returns>
    XdmNode? GetNode(NodeId nodeId);
}
