using System.Globalization;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;

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

    /// <summary>
    /// The declarations to write on the root element of a serialization: its whole in-scope set. A parsed
    /// element records only the xmlns attributes physically on it, so written on its own it lost the
    /// bindings it inherits from its ancestors (#57: fn-union-node-args-015 dropped xmlns:foo and xmlns:xsi).
    /// Descendants need nothing extra — their ancestors' bindings are already in scope in the output.
    /// </summary>
    /// <summary>
    /// True if <paramref name="prefix"/> is already bound to <paramref name="uri"/> in the output being
    /// written. XmlWriter.LookupPrefix(uri) answers a different question — which ONE prefix is bound to the
    /// URI — so when the default namespace and p share a URI it returns "" and xmlns:p was written again on
    /// every copied descendant (#57).
    /// </summary>
    internal static bool IsInScope(IReadOnlyDictionary<string, string>? scope, string? prefix, string uri)
        => scope != null && scope.TryGetValue(prefix ?? string.Empty, out var bound) && bound == uri;

    /// <summary>
    /// Records that <paramref name="prefix"/> is bound to <paramref name="uri"/> for an element's children,
    /// copying the parent's scope only when the element changes it.
    /// </summary>
    internal static Dictionary<string, string>? Bind(IReadOnlyDictionary<string, string>? scope,
        Dictionary<string, string>? owned, string? prefix, string uri)
    {
        var key = prefix ?? string.Empty;
        if (owned == null)
        {
            if (IsInScope(scope, key, uri))
                return null;
            owned = scope == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(scope, StringComparer.Ordinal);
        }
        owned[key] = uri;
        return owned;
    }

    internal static IReadOnlyList<NamespaceBinding> InScopeDeclarations(XdmElement element, Func<NodeId, XdmNode?> resolveParent)
        => Execution.AxisNavigationOperator.GatherInScopeNamespaces(element, resolveParent)
            .Select(binding => new NamespaceBinding(binding.Prefix, binding.Namespace))
            .ToList();
}
