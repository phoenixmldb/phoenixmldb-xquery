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

/// <summary>
/// Provides document-level metadata resolution for the XQuery execution engine.
/// </summary>
/// <remarks>
/// <para>
/// Metadata is key-value data associated with a document but stored outside the XML content itself —
/// for example, creation timestamps, access control tags, or application-specific properties.
/// The XQuery engine calls this provider when queries reference metadata functions.
/// </para>
/// <para>
/// Like <see cref="INodeProvider"/>, most developers do not need to implement this interface.
/// The built-in PhoenixmlDb storage layer provides its own implementation. Implement it when you
/// need to expose custom metadata from an external system to XQuery expressions.
/// </para>
/// </remarks>
/// <seealso cref="DelegateMetadataProvider"/>
/// <seealso cref="Execution.QueryEngine"/>
public interface IMetadataProvider
{
    /// <summary>
    /// Resolves a single metadata value by document ID and key.
    /// </summary>
    /// <param name="documentId">The document to query.</param>
    /// <param name="key">The metadata key (case-sensitive).</param>
    /// <returns>The raw byte value, or <c>null</c> if the key does not exist for this document.</returns>
    byte[]? GetMetadata(DocumentId documentId, string key);

    /// <summary>
    /// Resolves all metadata key-value pairs for a document.
    /// </summary>
    /// <param name="documentId">The document to query.</param>
    /// <returns>All metadata key-value pairs. Returns an empty sequence if the document has no metadata.</returns>
    IEnumerable<(string Key, byte[] Value)> GetAllMetadata(DocumentId documentId);
}

/// <summary>
/// Adapter that wraps a <see cref="Func{NodeId, XdmNode}"/> delegate into the <see cref="INodeProvider"/> interface.
/// </summary>
/// <remarks>
/// Provides a lightweight way to supply node resolution without implementing a full class.
/// Useful for testing, in-memory scenarios, or bridging to existing lookup functions.
/// </remarks>
/// <example>
/// <code>
/// var provider = new DelegateNodeProvider(id => myStore.LoadNode(id));
/// var engine = new QueryEngine(nodeProvider: provider);
/// </code>
/// </example>
public sealed class DelegateNodeProvider : INodeProvider
{
    private readonly Func<NodeId, XdmNode?> _loader;

    public DelegateNodeProvider(Func<NodeId, XdmNode?> loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public XdmNode? GetNode(NodeId nodeId) => _loader(nodeId);
}

/// <summary>
/// Adapter that wraps delegate-based metadata resolution into the <see cref="IMetadataProvider"/> interface.
/// </summary>
/// <remarks>
/// Provides a lightweight way to supply metadata resolution without implementing a full class.
/// Both a single-key resolver and an all-keys resolver must be supplied.
/// </remarks>
public sealed class DelegateMetadataProvider : IMetadataProvider
{
    private readonly Func<DocumentId, string, byte[]?> _resolver;
    private readonly Func<DocumentId, IEnumerable<(string Key, byte[] Value)>> _allResolver;

    public DelegateMetadataProvider(
        Func<DocumentId, string, byte[]?> resolver,
        Func<DocumentId, IEnumerable<(string Key, byte[] Value)>> allResolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _allResolver = allResolver ?? throw new ArgumentNullException(nameof(allResolver));
    }

    public byte[]? GetMetadata(DocumentId documentId, string key) => _resolver(documentId, key);

    public IEnumerable<(string Key, byte[] Value)> GetAllMetadata(DocumentId documentId) => _allResolver(documentId);
}
