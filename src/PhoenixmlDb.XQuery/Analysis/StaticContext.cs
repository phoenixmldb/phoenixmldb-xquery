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
    /// Base URI for resolving relative URIs. Mutable so the analyzer can temporarily
    /// swap it when descending into a nested module import (the nested module's
    /// relative `at` URIs must resolve against the nested module's own location, not
    /// the outer importer's base).
    /// </summary>
    public string? BaseUri { get; set; }

    /// <summary>
    /// Optional external module registry mapping module namespace URI → file path(s). Consulted
    /// by static analysis as a fallback when an <c>import module</c> declaration's location hints
    /// fail to resolve (or are absent). Multiple files per namespace are supported.
    /// </summary>
    public IReadOnlyDictionary<string, List<string>>? ExternalModules { get; init; }

    /// <summary>
    /// Maps location hint URIs to absolute file paths. Allows resolution of non-filesystem
    /// location hints (e.g. http:// URIs used as module identifiers in test suites).
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExternalModuleLocations { get; init; }

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
    /// Schema provider for schema-aware processing. The default <see cref="Execution.QueryEngine"/>
    /// constructor wires up an <see cref="XsdSchemaProvider"/> automatically; callers can
    /// pass a custom <see cref="ISchemaProvider"/> implementation, or explicitly <c>null</c>
    /// to disable schema features (rare opt-out — every <c>schema-element/attribute</c>
    /// reference becomes XPST0008 and every <c>validate</c> raises XQDY0027).
    /// </summary>
    public ISchemaProvider? SchemaProvider { get; init; }

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
    internal void RegisterGlobalVariable(QName name, XdmSequenceType? type, bool isModulePrivate = false)
    {
        var key = MakeVariableKey(name);

        GlobalVariables[key] = new VariableBinding
        {
            Name = name,
            Type = type ?? XdmSequenceType.ZeroOrMoreItems,
            Scope = VariableScope.Global,
            IsModulePrivate = isModulePrivate
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
