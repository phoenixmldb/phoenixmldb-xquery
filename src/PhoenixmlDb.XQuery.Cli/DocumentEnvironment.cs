using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Parsing;

namespace PhoenixmlDb.XQuery.Cli;

/// <summary>
/// Manages in-memory XDM documents for standalone XQuery execution.
/// Provides both <see cref="INodeProvider"/> and <see cref="IDocumentResolver"/>.
/// </summary>
internal sealed class DocumentEnvironment : INodeProvider, IDocumentResolver
{
    private readonly Dictionary<NodeId, XdmNode> _nodes = new();
    private readonly Dictionary<string, XdmDocument> _documentsByUri = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<XdmDocument> _allDocuments = new();
    private readonly ConcurrentDictionary<string, NamespaceId> _namespaces = new();
    private uint _nextNamespaceId = 100;
    private ulong _nextDocumentId = 1;
    private ulong _nextNodeIdBase;

    /// <summary>
    /// All loaded documents.
    /// </summary>
    public IReadOnlyList<XdmDocument> Documents => _allDocuments;

    /// <summary>
    /// Loads an XML document from a file path.
    /// </summary>
    public XdmDocument LoadFile(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var uri = new Uri(fullPath).AbsoluteUri;

        if (_documentsByUri.TryGetValue(uri, out var existing))
            return existing;

        using var stream = File.OpenRead(fullPath);
        return LoadFromStream(stream, new Uri(uri));
    }

    /// <summary>
    /// Loads an XML document from a stream.
    /// </summary>
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
    /// Loads an XML document from a string.
    /// </summary>
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
    /// Loads an XML document from a URL.
    /// </summary>
    public XdmDocument LoadFromUrl(string url)
    {
        if (_documentsByUri.TryGetValue(url, out var existing))
            return existing;

        using var client = new HttpClient();
        using var stream = client.GetStreamAsync(new Uri(url)).GetAwaiter().GetResult();
        return LoadFromStream(stream, new Uri(url));
    }

    /// <summary>
    /// Resolves a namespace URI to a namespace ID, interning as needed.
    /// </summary>
    public NamespaceId ResolveNamespace(string namespaceUri)
    {
        if (string.IsNullOrEmpty(namespaceUri))
            return NamespaceId.None;
        Interlocked.Increment(ref _nextNamespaceId);
        return _namespaces.GetOrAdd(namespaceUri, new NamespaceId(_nextNamespaceId));
    }

    /// <summary>
    /// Resolves a namespace ID back to its URI string.
    /// </summary>
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

    // INodeProvider
    public XdmNode? GetNode(NodeId nodeId)
    {
        return _nodes.GetValueOrDefault(nodeId);
    }

    // IDocumentResolver
    public XdmDocument? ResolveDocument(string uri)
    {
        // Check cache first
        if (_documentsByUri.TryGetValue(uri, out var cached))
            return cached;

        // Try as file path
        if (File.Exists(uri))
            return LoadFile(uri);

        // Try as relative path from current directory
        var fullPath = Path.GetFullPath(uri);
        if (File.Exists(fullPath))
            return LoadFile(fullPath);

        // Try as URL
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) &&
            (parsedUri.Scheme == "http" || parsedUri.Scheme == "https"))
        {
            return LoadFromUrl(new Uri(uri));
        }

        return null;
    }

    public bool IsDocumentAvailable(string uri)
    {
        if (_documentsByUri.ContainsKey(uri))
            return true;

        if (File.Exists(uri))
            return true;

        var fullPath = Path.GetFullPath(uri);
        if (File.Exists(fullPath))
            return true;

        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) &&
            (parsedUri.Scheme == "http" || parsedUri.Scheme == "https"))
        {
            return true; // Optimistic for URLs
        }

        return false;
    }

    public IEnumerable<XdmNode> ResolveCollection(string? uri)
    {
        if (uri == null)
        {
            // Default collection = all loaded documents
            return _allDocuments;
        }

        // If URI is a directory, load all XML files from it
        if (Directory.Exists(uri))
        {
            return LoadDirectory(uri);
        }

        // Single document as a collection
        var doc = ResolveDocument(uri);
        return doc != null ? [doc] : [];
    }

    /// <summary>
    /// Loads all XML files from a directory.
    /// </summary>
    public IReadOnlyList<XdmDocument> LoadDirectory(string directoryPath)
    {
        var docs = new List<XdmDocument>();
        var xmlFiles = Directory.GetFiles(directoryPath, "*.xml", SearchOption.AllDirectories);

        foreach (var file in xmlFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                docs.Add(LoadFile(file));
            }
            catch (System.Xml.XmlException)
            {
                // Skip malformed XML files silently in directory scan
            }
        }

        return docs;
    }

    public XdmDocument LoadFromStream(Stream stream, Uri? documentUri = null)
    {
        return LoadFromStream(stream, documentUri?.ToString());
    }

    public XdmDocument LoadFromString(string xml, Uri? documentUri = null)
    {
        return LoadFromString(xml, documentUri?.ToString());
    }

    public XdmDocument LoadFromUrl(Uri url)
    {
        return LoadFromUrl(url?.ToString() ?? string.Empty);
    }

    public NamespaceId ResolveNamespace(Uri namespaceUri)
    {
        throw new NotImplementedException();
    }
}
