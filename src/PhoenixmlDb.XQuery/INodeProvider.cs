using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Provides node resolution for the XQuery execution engine.
/// Implementations can load nodes from storage, in-memory documents,
/// or any other source.
/// </summary>
public interface INodeProvider
{
    /// <summary>
    /// Loads a node by its identifier.
    /// </summary>
    /// <param name="nodeId">The identifier of the node to load.</param>
    /// <returns>The node, or null if not found.</returns>
    XdmNode? GetNode(NodeId nodeId);
}

/// <summary>
/// Provides document metadata resolution for the XQuery execution engine.
/// </summary>
public interface IMetadataProvider
{
    /// <summary>
    /// Resolves a single metadata value by document ID and key.
    /// </summary>
    /// <param name="documentId">The document to query.</param>
    /// <param name="key">The metadata key.</param>
    /// <returns>The raw byte value, or null if the key does not exist.</returns>
    byte[]? GetMetadata(DocumentId documentId, string key);

    /// <summary>
    /// Resolves all metadata key-value pairs for a document.
    /// </summary>
    /// <param name="documentId">The document to query.</param>
    /// <returns>All metadata key-value pairs.</returns>
    IEnumerable<(string Key, byte[] Value)> GetAllMetadata(DocumentId documentId);
}

/// <summary>
/// Adapter that wraps delegate-based node loading into the <see cref="INodeProvider"/> interface.
/// </summary>
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
