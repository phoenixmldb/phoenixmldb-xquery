using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

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
