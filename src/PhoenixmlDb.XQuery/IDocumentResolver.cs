using System.Diagnostics.CodeAnalysis;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Resolves document URIs to XDM documents for the XQuery <c>fn:doc()</c> and <c>fn:collection()</c> functions.
/// </summary>
/// <remarks>
/// <para>
/// This is the extension point for document resolution in the XQuery engine. When a query calls
/// <c>doc("orders.xml")</c> or <c>collection("archive")</c>, the engine delegates to this interface
/// to locate and load the documents.
/// </para>
/// <para>
/// Implementations can load documents from the filesystem, HTTP URLs, databases, in-memory caches,
/// or any other source. The URI scheme is entirely up to the implementation.
/// </para>
/// <para>
/// Most developers do not need to implement this interface — the built-in PhoenixmlDb storage layer
/// provides its own implementation. Implement it when you need to expose external documents to XQuery
/// expressions, such as for cross-system joins or federated queries.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class FileDocumentResolver : IDocumentResolver
/// {
///     public XdmDocument? ResolveDocument(string uri)
///     {
///         if (!File.Exists(uri)) return null;
///         return XdmDocument.Load(File.OpenRead(uri));
///     }
///
///     public bool IsDocumentAvailable(string uri) => File.Exists(uri);
///
///     public IEnumerable&lt;XdmNode&gt; ResolveCollection(string? uri)
///     {
///         var dir = uri ?? "default-collection";
///         return Directory.GetFiles(dir, "*.xml").Select(f => ResolveDocument(f)!);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="Execution.QueryEngine"/>
public interface IDocumentResolver
{
    /// <summary>
    /// Loads a document by URI, corresponding to the XQuery <c>fn:doc()</c> function.
    /// </summary>
    /// <param name="uri">The document URI (file path, URL, or logical name).</param>
    /// <returns>The document node, or <c>null</c> if the document cannot be resolved.</returns>
    XdmDocument? ResolveDocument(string uri);

    /// <summary>
    /// Checks whether a document is available at the given URI without loading it,
    /// corresponding to the XQuery <c>fn:doc-available()</c> function.
    /// </summary>
    /// <param name="uri">The document URI.</param>
    /// <returns><c>true</c> if the document exists and can be loaded; <c>false</c> otherwise.</returns>
    bool IsDocumentAvailable(string uri);

    /// <summary>
    /// Returns all documents in a named collection, corresponding to the XQuery <c>fn:collection()</c> function.
    /// </summary>
    /// <param name="uri">The collection URI, or <c>null</c> for the default collection.</param>
    /// <returns>All document nodes in the collection. Returns an empty sequence if the collection is empty or unknown.</returns>
    /// <remarks>
    /// The engine calls <see cref="TryResolveCollection"/>, which can tell an empty collection
    /// from an unknown one; its default implementation calls this member.
    /// </remarks>
    IEnumerable<XdmNode> ResolveCollection(string? uri);

    /// <summary>
    /// Returns the documents of a collection and says whether the collection exists, for the
    /// XQuery <c>fn:collection()</c> and <c>fn:uri-collection()</c> functions.
    /// </summary>
    /// <param name="uri">The collection URI, or <c>null</c> for the default collection.</param>
    /// <param name="documents">
    /// When this returns <c>true</c>, the document nodes of the collection, which may be none.
    /// </param>
    /// <returns>
    /// <c>true</c> when the collection exists: the functions return its documents, and an empty
    /// collection gives the empty sequence. <c>false</c> when there is no such collection (no
    /// default collection, or a URI that names none): the functions raise FODC0002.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The default implementation calls <see cref="ResolveCollection"/> and reports a
    /// collection that gives no documents as unknown, as the engine did before this member
    /// existed. Override it in a resolver whose collections can be empty.
    /// </para>
    /// <para>
    /// A resolver that delegates to another resolver should forward this member as well as
    /// <see cref="ResolveCollection"/>: with the default implementation it reports an empty
    /// collection of the other resolver as unknown.
    /// </para>
    /// </remarks>
    bool TryResolveCollection(string? uri, [NotNullWhen(true)] out IEnumerable<XdmNode>? documents)
    {
        var resolved = ResolveCollection(uri).ToArray();
        documents = resolved.Length > 0 ? resolved : null;
        return resolved.Length > 0;
    }
}
