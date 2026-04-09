using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Static context for XQuery query analysis.
/// Contains namespace bindings, function library, and other compile-time information.
/// </summary>
public sealed class StaticContext
{
    /// <summary>
    /// Namespace bindings (prefix → URI).
    /// </summary>
    public NamespaceContext Namespaces { get; init; } = new();

    /// <summary>
    /// Available functions.
    /// </summary>
    public FunctionLibrary Functions { get; init; } = FunctionLibrary.Standard;

    /// <summary>
    /// Static type of the context item (if known).
    /// </summary>
    public XdmSequenceType? ContextItemType { get; init; }

    /// <summary>
    /// Base URI for resolving relative URIs.
    /// </summary>
    public string? BaseUri { get; init; }

    /// <summary>
    /// Optional external module registry mapping module namespace URI → file path. Consulted
    /// by static analysis as a fallback when an <c>import module</c> declaration's location hints
    /// fail to resolve (or are absent).
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExternalModules { get; init; }

    /// <summary>
    /// Default element/type namespace.
    /// </summary>
    public string? DefaultElementNamespace { get; init; }

    /// <summary>
    /// Default function namespace.
    /// </summary>
    public string DefaultFunctionNamespace { get; init; } = WellKnownNamespaces.FnUri;

    /// <summary>
    /// Collation for string comparisons.
    /// </summary>
    public string DefaultCollation { get; init; } = "http://www.w3.org/2005/xpath-functions/collation/codepoint";

    /// <summary>
    /// Construction mode (preserve or strip type annotations).
    /// </summary>
    public ConstructionMode ConstructionMode { get; init; } = ConstructionMode.Preserve;

    /// <summary>
    /// Ordering mode for unordered sequences.
    /// </summary>
    public OrderingMode OrderingMode { get; init; } = OrderingMode.Ordered;

    /// <summary>
    /// How to handle empty sequences in order by.
    /// </summary>
    public EmptyOrder DefaultEmptyOrder { get; init; } = EmptyOrder.Least;

    /// <summary>
    /// Boundary space handling.
    /// </summary>
    public BoundarySpace BoundarySpace { get; init; } = BoundarySpace.Strip;

    /// <summary>
    /// Copy-namespaces mode.
    /// </summary>
    public CopyNamespacesMode CopyNamespacesMode { get; init; } =
        CopyNamespacesMode.PreserveInherit;

    /// <summary>
    /// Imported library modules, keyed by namespace URI.
    /// Populated during static analysis when import module declarations are resolved.
    /// </summary>
    internal Dictionary<string, Ast.ModuleExpression> ImportedModules { get; } = new();

    /// <summary>
    /// Global variables declared in the prolog (registered during pre-analysis).
    /// </summary>
    internal Dictionary<string, VariableBinding> GlobalVariables { get; } = new();

    /// <summary>
    /// Registers a global variable from a prolog declaration.
    /// </summary>
    internal void RegisterGlobalVariable(QName name, XdmSequenceType? type)
    {
        var key = MakeVariableKey(name);

        GlobalVariables[key] = new VariableBinding
        {
            Name = name,
            Type = type ?? XdmSequenceType.ZeroOrMoreItems,
            Scope = VariableScope.Global
        };
    }

    /// <summary>
    /// Creates a default static context.
    /// </summary>
    public static StaticContext Default { get; } = new();

    /// <summary>
    /// Computes a canonical URI-based lookup key for a variable QName.
    /// Ensures $p:v (prefix resolved to uri) and $Q{uri}v produce the same key.
    /// </summary>
    internal string MakeVariableKey(QName name)
    {
        // Prefer ExpandedNamespace if set (EQName syntax or pre-resolved).
        var uri = name.ExpandedNamespace;
        if (string.IsNullOrEmpty(uri) && name.Namespace != NamespaceId.None)
            uri = Namespaces.GetUri(name.Namespace);
        if (string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(name.Prefix))
            uri = Namespaces.ResolvePrefix(name.Prefix!);
        return string.IsNullOrEmpty(uri) ? name.LocalName : $"{uri}:{name.LocalName}";
    }
}

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

        // Register well-known URI → NamespaceId mappings so that
        // GetOrCreateId returns the correct IDs used by FunctionLibrary.
        _uriToId[WellKnownNamespaces.XsUri] = Functions.FunctionNamespaces.Xs;
        _uriToId[WellKnownNamespaces.FnUri] = Functions.FunctionNamespaces.Fn;
        _uriToId[WellKnownNamespaces.MathUri] = Functions.FunctionNamespaces.Math;
        _uriToId[WellKnownNamespaces.MapUri] = Functions.FunctionNamespaces.Map;
        _uriToId[WellKnownNamespaces.ArrayUri] = Functions.FunctionNamespaces.Array;
        _uriToId[WellKnownNamespaces.LocalUri] = Functions.FunctionNamespaces.Local;
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
    /// Resolves a prefix to a URI.
    /// </summary>
    public string? ResolvePrefix(string prefix)
    {
        return _prefixToUri.GetValueOrDefault(prefix);
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

/// <summary>
/// Well-known namespace URIs.
/// </summary>
public static class WellKnownNamespaces
{
    public const string XmlUri = "http://www.w3.org/XML/1998/namespace";
    public const string XsUri = "http://www.w3.org/2001/XMLSchema";
    public const string XsiUri = "http://www.w3.org/2001/XMLSchema-instance";
    public const string FnUri = "http://www.w3.org/2005/xpath-functions";
    public const string LocalUri = "http://www.w3.org/2005/xquery-local-functions";
    public const string MapUri = "http://www.w3.org/2005/xpath-functions/map";
    public const string ArrayUri = "http://www.w3.org/2005/xpath-functions/array";
    public const string MathUri = "http://www.w3.org/2005/xpath-functions/math";
    public const string ErrUri = "http://www.w3.org/2005/xqt-errors";
}

/// <summary>
/// Construction mode for element constructors.
/// </summary>
public enum ConstructionMode
{
    Preserve,
    Strip
}

/// <summary>
/// Ordering mode.
/// </summary>
public enum OrderingMode
{
    Ordered,
    Unordered
}

/// <summary>
/// Boundary space handling.
/// </summary>
public enum BoundarySpace
{
    Preserve,
    Strip
}

/// <summary>
/// Copy-namespaces mode.
/// </summary>
public enum CopyNamespacesMode
{
    PreserveInherit,
    PreserveNoInherit,
    NoPreserveInherit,
    NoPreserveNoInherit
}
