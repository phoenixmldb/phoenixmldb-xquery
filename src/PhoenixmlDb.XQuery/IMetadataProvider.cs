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
    /// Resolves the values of one metadata key by document ID.
    /// </summary>
    /// <param name="documentId">The document to query.</param>
    /// <param name="key">
    /// The metadata key (case-sensitive): an unprefixed local name, to which the host applies its default metadata
    /// namespace, or an expanded name <c>Q{uri}local</c>. It never carries a prefix, because <c>phx:metadata</c>
    /// resolves prefixes first: <c>dbxml:size</c> arrives as <c>Q{https://schemas.phoenixml.dev/2026/meta}size</c>,
    /// and the Clark form <c>{uri}local</c> arrives as <c>Q{uri}local</c>.
    /// </param>
    /// <returns>
    /// Every value of the key, each at its stored type, in stored order: one value for a scalar key, all of them
    /// for a key that holds a set. An empty list when the document has no value for the key.
    /// <c>phx:metadata($node, $key)</c> returns these values as its result sequence.
    /// </returns>
    /// <remarks>
    /// Changed in 3.3.0: it returned the raw bytes of one value, which <c>phx:metadata</c> read as a string (and as an
    /// <c>xs:integer</c> for <c>size</c> and <c>node-count</c> only).
    /// </remarks>
    IReadOnlyList<Xdm.XdmValue> GetMetadata(DocumentId documentId, string key);

    /// <summary>
    /// Resolves every metadata key of a document with its values.
    /// </summary>
    /// <param name="documentId">The document to query.</param>
    /// <returns>
    /// Each key with its values, typed and in stored order (see <see cref="GetMetadata"/>). An empty sequence if the
    /// document has no metadata. <c>phx:metadata($node)</c> returns a map from each key to its values.
    /// </returns>
    IEnumerable<(string Key, IReadOnlyList<Xdm.XdmValue> Values)> GetAllMetadata(DocumentId documentId);
}
