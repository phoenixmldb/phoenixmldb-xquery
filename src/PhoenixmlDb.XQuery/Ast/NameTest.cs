using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Tests node by name (e.g., customer, @id, ns:element).
/// </summary>
public sealed class NameTest : NodeTest
{
    /// <summary>
    /// Namespace URI (null for no namespace, "*" for any namespace).
    /// Set at parse time from prefix resolution, used to resolve NamespaceId at execution time.
    /// </summary>
    public string? NamespaceUri { get; set; }

    /// <summary>
    /// Resolved namespace ID (set during static analysis or at transformation time).
    /// </summary>
    public NamespaceId? ResolvedNamespace { get; internal set; }

    /// <summary>
    /// Local name ("*" for any local name).
    /// </summary>
    public required string LocalName { get; init; }

    /// <summary>
    /// Original prefix from query (for error messages).
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// True if this is a wildcard for local name (*).
    /// </summary>
    public bool IsLocalNameWildcard => LocalName == "*";

    /// <summary>
    /// True if this is a wildcard for namespace (*:name or *:*).
    /// </summary>
    public bool IsNamespaceWildcard => NamespaceUri == "*";

    public override bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName)
    {
        // Check local name
        if (!IsLocalNameWildcard && localName != LocalName)
            return false;

        // Check namespace
        if (IsNamespaceWildcard)
        {
            // *:NCName - any namespace matches
            return true;
        }

        if (ResolvedNamespace.HasValue)
        {
            // Resolved namespace - compare by ID
            return ns == ResolvedNamespace;
        }

        // No resolved namespace - check NamespaceUri string
        if (string.IsNullOrEmpty(NamespaceUri))
        {
            // Bare wildcard `*` matches any element regardless of namespace
            if (IsLocalNameWildcard)
                return true;
            // Unprefixed specific name: match only elements in the null namespace.
            // Per XSLT spec, without xpath-default-namespace, unprefixed element names
            // in patterns match elements in no namespace.
            return !ns.HasValue || ns.Value == NamespaceId.None;
        }

        // Pattern has explicit NamespaceUri but ResolvedNamespace wasn't set
        // This shouldn't happen if namespace resolution is working correctly
        // Return false as we can't verify the namespace
        return false;
    }

    /// <summary>
    /// Resolves the namespace URI to a NamespaceId using the provided resolver.
    /// </summary>
    public void ResolveNamespace(Func<string, NamespaceId> namespaceResolver)
    {
        if (!string.IsNullOrEmpty(NamespaceUri) && NamespaceUri != "*")
        {
            ResolvedNamespace = namespaceResolver(NamespaceUri);
        }
    }

    public override string ToString()
    {
        if (Prefix != null)
            return $"{Prefix}:{LocalName}";
        if (NamespaceUri == "*")
            return $"*:{LocalName}";
        return LocalName;
    }
}
