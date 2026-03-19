using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Resolves document URIs to XDM documents for fn:doc() and fn:collection().
/// Implementations can load from files, URLs, databases, or any other source.
/// </summary>
public interface IDocumentResolver
{
    /// <summary>
    /// Loads a document by URI.
    /// </summary>
    /// <param name="uri">The document URI (file path, URL, or logical name).</param>
    /// <returns>The document node, or null if the document cannot be resolved.</returns>
    XdmDocument? ResolveDocument(string uri);

    /// <summary>
    /// Checks whether a document is available at the given URI.
    /// </summary>
    /// <param name="uri">The document URI.</param>
    /// <returns>True if the document exists and can be loaded.</returns>
    bool IsDocumentAvailable(string uri);

    /// <summary>
    /// Returns all documents in a named collection.
    /// </summary>
    /// <param name="uri">The collection URI, or null for the default collection.</param>
    /// <returns>All document nodes in the collection.</returns>
    IEnumerable<XdmNode> ResolveCollection(string? uri);
}
