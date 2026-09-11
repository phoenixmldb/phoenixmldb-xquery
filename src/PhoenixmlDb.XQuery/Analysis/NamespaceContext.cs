using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Namespace bindings for prefix resolution.
/// </summary>
public sealed class NamespaceContext
{
    private readonly Dictionary<string, string> _prefixToUri = new();
    private readonly Dictionary<string, NamespaceId> _uriToId = new();
    private readonly Dictionary<NamespaceId, string> _idToUri = new();

    public NamespaceContext()
    {
        // Register well-known namespaces (prefix → URI)
        RegisterNamespace("xml", WellKnownNamespaces.XmlUri);
        RegisterNamespace("xs", WellKnownNamespaces.XsUri);
        RegisterNamespace("xsi", WellKnownNamespaces.XsiUri);
        RegisterNamespace("fn", WellKnownNamespaces.FnUri);
        RegisterNamespace("local", WellKnownNamespaces.LocalUri);
        RegisterNamespace("map", WellKnownNamespaces.MapUri);
        RegisterNamespace("array", WellKnownNamespaces.ArrayUri);
        RegisterNamespace("math", WellKnownNamespaces.MathUri);
        RegisterNamespace("err", WellKnownNamespaces.ErrUri);

        // Register well-known URI → NamespaceId mappings so that
        // GetOrCreateId returns the correct IDs used by FunctionLibrary.
        foreach (var (uri, id) in Functions.FunctionNamespaces.WellKnown)
            _uriToId[uri] = id;
        foreach (var (u, i) in _uriToId)
            _idToUri[i] = u;
    }

    /// <summary>
    /// Registers a namespace prefix.
    /// </summary>
    public void RegisterNamespace(string prefix, string uri)
    {
        _prefixToUri[prefix] = uri;
    }

    /// <summary>
    /// Removes a prefix binding (used by NamespaceResolver to unwind lexically-scoped
    /// xmlns declarations on direct element constructors).
    /// </summary>
    public void UnregisterNamespace(string prefix)
    {
        _prefixToUri.Remove(prefix);
    }

    /// <summary>
    /// Takes a snapshot of all current prefix→URI bindings. Used to save/restore
    /// namespace state when processing imported modules (their namespace declarations
    /// must not leak to the importing module).
    /// </summary>
    public Dictionary<string, string> SnapshotPrefixes()
        => new(_prefixToUri);

    /// <summary>
    /// Restores prefix→URI bindings to a previously captured snapshot.
    /// Any prefixes not in the snapshot are removed; any in the snapshot are restored.
    /// </summary>
    public void RestorePrefixes(Dictionary<string, string> snapshot)
    {
        // Remove prefixes that weren't in the snapshot
        var toRemove = new List<string>();
        foreach (var prefix in _prefixToUri.Keys)
        {
            if (!snapshot.ContainsKey(prefix))
                toRemove.Add(prefix);
        }
        foreach (var p in toRemove)
            _prefixToUri.Remove(p);
        // Restore/add prefixes from snapshot
        foreach (var (p, u) in snapshot)
            _prefixToUri[p] = u;
    }

    /// <summary>
    /// Resolves a prefix to a URI.
    /// </summary>
    public string? ResolvePrefix(string prefix)
    {
        var uri = _prefixToUri.GetValueOrDefault(prefix);
        // A non-default prefix bound to the zero-length URI is effectively undeclared
        // (XPST0081): return null so callers treat it as unbound.
        // However, the default namespace prefix ("") bound to "" is a valid undeclaration
        // (xmlns="") that overrides any prolog default element namespace. Return "" for
        // the default prefix so callers don't fall through to ##default-element.
        if (uri is { Length: 0 } && !string.IsNullOrEmpty(prefix))
            return null;
        return uri;
    }

    /// <summary>
    /// Gets or creates a NamespaceId for a URI.
    /// </summary>
    public NamespaceId GetOrCreateId(string uri)
    {
        if (_uriToId.TryGetValue(uri, out var id))
            return id;

        // Assign a new ID (in production, this would use the NamespaceManager)
        id = new NamespaceId((uint)_uriToId.Count + 100);
        _uriToId[uri] = id;
        _idToUri[id] = uri;
        return id;
    }

    /// <summary>
    /// Returns the namespace URI for an ID, or null if unknown.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "XML namespace URIs are handled as strings throughout the codebase.")]
    public string? GetUri(NamespaceId id)
    {
        return _idToUri.TryGetValue(id, out var uri) ? uri : null;
    }

    /// <summary>
    /// Gets all registered prefixes.
    /// </summary>
    public IEnumerable<string> Prefixes => _prefixToUri.Keys;
}
