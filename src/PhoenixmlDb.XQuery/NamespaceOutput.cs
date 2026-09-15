using System.Globalization;
using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Resolves the namespace ids of nodes being serialized. A node store resolves the ids it allocated;
/// the well-known ids are permanent (<see cref="NamespaceRegistry"/>) and resolve even in a store
/// that never registered them. Any other id that resolves nowhere is a defect upstream — an id from
/// another store or compilation — and fails loudly: the serializers used to write it as "" or
/// "urn:unresolved:p", silently putting the node in a different namespace (or printing
/// xmlns:p="", which XML 1.0 forbids).
/// </summary>
internal static class NamespaceOutput
{
    /// <param name="id">The namespace id on the node or declaration.</param>
    /// <param name="resolved">What the node store returned for <paramref name="id"/>.</param>
    /// <param name="name">The name being written, for the error message.</param>
    internal static string UriFor(NamespaceId id, string? resolved, string name)
    {
        if (resolved != null)
            return resolved;
        if (id == NamespaceId.None)
            return string.Empty;
        return NamespaceRegistry.GetUri(id) ?? throw new InvalidOperationException(
            $"Cannot serialize '{name}': namespace id {id.Value.ToString(CultureInfo.InvariantCulture)} is not " +
            "registered with the node store. The node was built with an id from another store or compilation; " +
            "writing it with an empty or placeholder URI would change its namespace.");
    }

    internal static string QualifiedName(string? prefix, string localName)
        => string.IsNullOrEmpty(prefix) ? localName : prefix + ":" + localName;

    internal static string DeclarationName(string? prefix)
        => string.IsNullOrEmpty(prefix) ? "xmlns" : "xmlns:" + prefix;
}
