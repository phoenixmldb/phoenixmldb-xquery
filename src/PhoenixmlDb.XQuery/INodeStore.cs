using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

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
/// <summary>
/// Extended node provider with read-only tree navigation capabilities.
/// Adds namespace resolution and attribute access beyond basic node lookup.
/// </summary>
/// <remarks>
/// Implement this interface when functions like <c>fn:path</c>, <c>fn:id</c>, or <c>fn:xml-to-json</c>
/// need to navigate the tree and resolve namespace URIs. The XSLT and XQuery engines each provide
/// their own implementations backed by their respective node stores.
/// </remarks>
public interface INodeStore : INodeProvider
{
    /// <summary>
    /// Resolves a <see cref="NamespaceId"/> to its namespace URI string.
    /// </summary>
    /// <param name="id">The namespace identifier to resolve.</param>
    /// <returns>The namespace URI, or <c>null</c> if the identifier is unknown.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1055:URI-like return values should not be strings")]
    string? GetNamespaceUri(NamespaceId id);

    /// <summary>
    /// Returns the attributes of an element node.
    /// </summary>
    /// <param name="element">The element whose attributes to return.</param>
    /// <returns>The element's attributes, resolved from its attribute ID list.</returns>
    IEnumerable<XdmAttribute> GetAttributes(XdmElement element);
}
