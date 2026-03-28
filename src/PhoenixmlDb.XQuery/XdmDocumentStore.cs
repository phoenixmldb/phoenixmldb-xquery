using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Parsing;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// An in-memory XDM document store that manages parsed XML documents, node resolution,
/// and namespace interning. Implements both <see cref="INodeProvider"/> and <see cref="IDocumentResolver"/>
/// so it can be passed directly to <see cref="Execution.QueryEngine"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the recommended way to set up standalone XQuery execution against in-memory XML.
/// Load documents via <see cref="LoadFromString"/> or <see cref="LoadFile"/>, then pass
/// this store as both the <c>nodeProvider</c> and <c>documentResolver</c> to
/// <see cref="Execution.QueryEngine"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var store = new XdmDocumentStore();
/// store.LoadFromString("&lt;root&gt;&lt;item/&gt;&lt;/root&gt;", "urn:my:doc");
///
/// var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
/// await foreach (var item in engine.ExecuteAsync("doc('urn:my:doc')//item"))
/// {
///     Console.WriteLine(item);
/// }
/// </code>
/// </example>
public sealed class XdmDocumentStore : INodeProvider, IDocumentResolver
{
    private readonly Dictionary<NodeId, XdmNode> _nodes = new();
    private readonly Dictionary<string, XdmDocument> _documentsByUri = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<XdmDocument> _allDocuments = new();
    private readonly ConcurrentDictionary<string, NamespaceId> _namespaces = new();
    private uint _nextNamespaceId = 100;
    private ulong _nextDocumentId = 1;
    private ulong _nextNodeIdBase = 1; // Start at 1; NodeId(0) == NodeId.None (sentinel)

    /// <summary>
    /// All loaded documents.
    /// </summary>
    public IReadOnlyList<XdmDocument> Documents => _allDocuments;

    /// <summary>
    /// Loads an XML document from a string.
    /// </summary>
    /// <param name="xml">The XML content to parse.</param>
    /// <param name="documentUri">
    /// Optional URI that identifies this document. When set, the document can be retrieved
    /// via <c>fn:doc()</c> using this URI.
    /// </param>
    /// <returns>The parsed XDM document node.</returns>
    public XdmDocument LoadFromString(string xml, string? documentUri = null)
    {
        var docId = new DocumentId(_nextDocumentId++);
        var startNodeId = new NodeId(_nextNodeIdBase);

        var parser = new XmlDocumentParser(docId, startNodeId, ResolveNamespace);
        var result = parser.Parse(xml, documentUri);

        _nextNodeIdBase += result.NodeCount + 1;

        foreach (var node in result.Nodes)
        {
            _nodes[node.Id] = node;
        }

        if (documentUri != null)
        {
            _documentsByUri[documentUri] = result.Document;
        }

        _allDocuments.Add(result.Document);
        return result.Document;
    }

    /// <summary>
    /// Loads an XML document from a file path.
    /// </summary>
    /// <param name="filePath">The path to the XML file.</param>
    /// <returns>The parsed XDM document node.</returns>
    public XdmDocument LoadFile(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var uri = new Uri(fullPath).AbsoluteUri;

        if (_documentsByUri.TryGetValue(uri, out var existing))
            return existing;

        using var stream = File.OpenRead(fullPath);
        return LoadFromStream(stream, uri);
    }

    /// <summary>
    /// Loads an XML document from a stream.
    /// </summary>
    /// <param name="stream">The stream containing XML content.</param>
    /// <param name="documentUri">Optional URI that identifies this document.</param>
    /// <returns>The parsed XDM document node.</returns>
    public XdmDocument LoadFromStream(Stream stream, string? documentUri = null)
    {
        var docId = new DocumentId(_nextDocumentId++);
        var startNodeId = new NodeId(_nextNodeIdBase);

        var parser = new XmlDocumentParser(docId, startNodeId, ResolveNamespace);
        var result = parser.Parse(stream, documentUri);

        _nextNodeIdBase += result.NodeCount + 1;

        foreach (var node in result.Nodes)
        {
            _nodes[node.Id] = node;
        }

        if (documentUri != null)
        {
            _documentsByUri[documentUri] = result.Document;
        }

        _allDocuments.Add(result.Document);
        return result.Document;
    }

    /// <summary>
    /// Resolves a namespace URI string to an interned <see cref="NamespaceId"/>.
    /// </summary>
    /// <param name="namespaceUri">The namespace URI to intern.</param>
    /// <returns>The interned namespace identifier.</returns>
    public NamespaceId ResolveNamespace(string namespaceUri)
    {
        if (string.IsNullOrEmpty(namespaceUri))
            return NamespaceId.None;
        Interlocked.Increment(ref _nextNamespaceId);
        return _namespaces.GetOrAdd(namespaceUri, new NamespaceId(_nextNamespaceId));
    }

    /// <summary>
    /// Registers a specific <see cref="NamespaceId"/> for a URI, so that elements constructed
    /// with IDs from the static analyzer can be serialized back to their namespace URIs.
    /// If the URI is already registered, this is a no-op (the existing ID is kept).
    /// </summary>
    public void RegisterNamespace(string namespaceUri, NamespaceId id)
    {
        if (!string.IsNullOrEmpty(namespaceUri))
            _namespaces.TryAdd(namespaceUri, id);
    }

    /// <summary>
    /// Resolves an interned <see cref="NamespaceId"/> back to its URI string.
    /// Used by <see cref="XQueryResultSerializer"/> for XML serialization.
    /// </summary>
    /// <param name="id">The namespace identifier.</param>
    /// <returns>The namespace URI, or <c>null</c> if the identifier is not known.</returns>
    public Uri? ResolveNamespaceUri(NamespaceId id)
    {
        if (id == NamespaceId.None)
            return null;

        foreach (var (uri, nsId) in _namespaces)
        {
            if (nsId == id)
                return new Uri(uri);
        }

        return null;
    }

    /// <summary>
    /// Allocates a new unique <see cref="NodeId"/> for constructed nodes (e.g., XQuery element/text constructors).
    /// </summary>
    /// <returns>A fresh node ID that does not collide with any existing node.</returns>
    public NodeId AllocateNodeId()
    {
        return new NodeId(_nextNodeIdBase++);
    }

    /// <summary>
    /// Registers a constructed node (created by XQuery node constructors) in the store,
    /// making it resolvable via <see cref="GetNode"/>.
    /// </summary>
    /// <param name="node">The node to register.</param>
    public void RegisterNode(XdmNode node)
    {
        _nodes[node.Id] = node;
    }

    // INodeProvider

    /// <inheritdoc />
    public XdmNode? GetNode(NodeId nodeId)
    {
        return _nodes.GetValueOrDefault(nodeId);
    }

    // IDocumentResolver

    /// <inheritdoc />
    public XdmDocument? ResolveDocument(string uri)
    {
        // Check cache first
        if (_documentsByUri.TryGetValue(uri, out var cached))
            return cached;

        // Convert file:// URIs to local paths
        var path = ToLocalPath(uri);

        if (File.Exists(path))
            return LoadFile(path);

        // Try as relative path from current directory
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            return LoadFile(fullPath);

        return null;
    }

    /// <inheritdoc />
    public bool IsDocumentAvailable(string uri)
    {
        if (_documentsByUri.ContainsKey(uri))
            return true;

        var path = ToLocalPath(uri);

        if (File.Exists(path))
            return true;

        var fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath);
    }

    /// <summary>
    /// Converts a file:// URI to a local file path. Returns the original string for non-file URIs.
    /// </summary>
    private static string ToLocalPath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) && parsedUri.IsFile)
            return parsedUri.LocalPath;
        return uri;
    }

    /// <inheritdoc />
    public IEnumerable<XdmNode> ResolveCollection(string? uri)
    {
        if (uri == null)
        {
            // Default collection = all loaded documents
            return _allDocuments;
        }

        // Single document as a collection
        var doc = ResolveDocument(uri);
        return doc != null ? [doc] : [];
    }
}
