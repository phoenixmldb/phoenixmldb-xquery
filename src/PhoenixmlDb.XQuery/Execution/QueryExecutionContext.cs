using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Configurable limits for query execution to prevent unbounded resource consumption.
/// </summary>
public sealed class QueryExecutionLimits
{
    /// <summary>
    /// Default execution limits.
    /// </summary>
    public static readonly QueryExecutionLimits Default = new();

    /// <summary>
    /// Maximum number of items that can be materialized into a single in-memory collection.
    /// Prevents unbounded sequences from exhausting memory.
    /// Default: 1,000,000.
    /// </summary>
    public int MaxResultItems { get; init; } = 2_000_000;

    /// <summary>
    /// Maximum recursion depth for tree traversal (e.g., descendant axis).
    /// Prevents stack overflow on deep or cyclic structures.
    /// Default: 1,000.
    /// </summary>
    public int MaxRecursionDepth { get; init; } = 2_000;
}

/// <summary>
/// Runtime context for query execution.
/// Implements the Ast.ExecutionContext interface.
/// </summary>
public sealed class QueryExecutionContext : Ast.ExecutionContext, IDisposable
{
    private readonly Stack<Scope> _scopes = new();
    private readonly Stack<Scope> _scopePool = new();
    private readonly Stack<object?> _contextItems = new();
    private readonly Stack<int> _positions = new();
    private readonly Stack<int> _sizes = new();
    private readonly FunctionLibrary _functions;
    private readonly INodeProvider? _nodeProvider;
    private readonly Stack<INodeProvider> _supplementaryNodeProviders = new();
    private readonly IMetadataProvider? _metadataProvider;
    private readonly IDocumentResolver? _documentResolver;
    private readonly DateTimeOffset _currentDateTime;
    private int _functionCallDepth;
    private readonly Dictionary<QName, object?> _externalVariables = new();

    /// <summary>
    /// Full-text relevance scores from the most recent contains-text evaluation.
    /// Maps node identity → score (0.0 to 1.0).
    /// </summary>
    private readonly Dictionary<int, double> _fullTextScores = [];

    /// <summary>
    /// Records a full-text score for a node (called by FtContainsOperator).
    /// </summary>
    public void SetFullTextScore(object? node, double score)
    {
        if (node != null)
            _fullTextScores[System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node)] = score;
    }

    /// <summary>
    /// Gets the full-text score for a node (called by ft:score()).
    /// </summary>
    public double GetFullTextScore(object? node)
    {
        if (node != null && _fullTextScores.TryGetValue(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node), out var score))
            return score;
        return 0.0;
    }

    /// <summary>
    /// Pending Update List for XQuery Update Facility.
    /// Collects update primitives during query evaluation.
    /// Applied atomically after the query completes.
    /// </summary>
    public Ast.PendingUpdateList PendingUpdates { get; set; } = new();

    public QueryExecutionContext(
        ContainerId container,
        FunctionLibrary? functions = null,
        INodeProvider? nodeProvider = null,
        IMetadataProvider? metadataProvider = null,
        IDocumentResolver? documentResolver = null,
        QueryExecutionLimits? limits = null,
        Func<NamespaceId, string?>? namespaceResolver = null,
        CancellationToken cancellationToken = default)
    {
        Container = container;
        _functions = functions ?? FunctionLibrary.Standard;
        _nodeProvider = nodeProvider;
        _metadataProvider = metadataProvider;
        _documentResolver = documentResolver;
        CancellationToken = cancellationToken;
        Limits = limits ?? QueryExecutionLimits.Default;
        _currentDateTime = DateTimeOffset.Now;
        NamespaceResolver = namespaceResolver;
        _scopes.Push(new Scope());
    }

    /// <summary>
    /// The container being queried.
    /// </summary>
    public ContainerId Container { get; }

    /// <summary>
    /// Cancellation token for the query.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Execution limits for this query.
    /// </summary>
    public QueryExecutionLimits Limits { get; }

    /// <summary>
    /// The node provider for loading nodes by ID.
    /// </summary>
    public INodeProvider? NodeProvider => _nodeProvider;

    /// <summary>
    /// Gets the node store for tree navigation, wrapping the node provider and namespace resolver.
    /// </summary>
    public INodeStore? NodeStore => _nodeStore ??= _nodeProvider != null
        ? (_nodeProvider is INodeStore ns ? ns : new NodeStoreAdapter(_nodeProvider, NamespaceResolver))
        : null;
    private INodeStore? _nodeStore;

    /// <summary>
    /// The metadata provider for resolving document metadata.
    /// </summary>
    public IMetadataProvider? MetadataProvider => _metadataProvider;

    /// <summary>
    /// The document resolver for fn:doc() and fn:collection().
    /// </summary>
    public IDocumentResolver? DocumentResolver => _documentResolver;

    /// <summary>
    /// Whether XPath 1.0 backwards-compatible mode is active.
    /// When true, general comparisons with boolean operands convert both to boolean.
    /// </summary>
    public bool BackwardsCompatible { get; set; }

    /// <summary>
    /// When true, the expression is being evaluated inside xsl:evaluate.
    /// XSLT-specific functions (system-property, current-output-uri, etc.) must raise XTDE3160.
    /// </summary>
    public bool InsideXslEvaluate { get; set; }

    /// <summary>
    /// The default collation URI for string comparisons and functions.
    /// When set to a case-insensitive collation, value comparisons and 2-arity
    /// string functions (starts-with, contains, etc.) use case-insensitive matching.
    /// </summary>
    public string? DefaultCollation { get; set; }

    /// <summary>
    /// Gets the current date/time (stable for the duration of the query).
    /// </summary>
    public DateTimeOffset CurrentDateTime => _currentDateTime;

    /// <summary>
    /// Gets the current date (stable for the duration of the query).
    /// </summary>
    public DateOnly CurrentDate => DateOnly.FromDateTime(_currentDateTime.DateTime);

    /// <summary>
    /// Gets the current time (stable for the duration of the query).
    /// </summary>
    public TimeOnly CurrentTime => TimeOnly.FromDateTime(_currentDateTime.DateTime);

    /// <summary>
    /// Gets the function library.
    /// </summary>
    public FunctionLibrary Functions => _functions;

    /// <summary>
    /// Resolves a NamespaceId to its URI string. Used by namespace-uri() and related functions.
    /// </summary>
    public Func<NamespaceId, string?>? NamespaceResolver { get; }

    /// <summary>
    /// In-scope namespace prefix bindings (prefix → URI) from the static context.
    /// Used by XSLT functions like system-property() to resolve prefixed QName string arguments.
    /// </summary>
    public IReadOnlyDictionary<string, string>? PrefixNamespaceBindings { get; set; }

    /// <summary>
    /// The static base URI for this execution context.
    /// Used by fn:static-base-uri() and fn:resolve-uri($relative).
    /// </summary>
    public string? StaticBaseUri { get; set; }

    /// <summary>
    /// Boundary-space policy from prolog: true = strip whitespace-only text in constructors.
    /// Default is false (preserve), matching XQuery 3.1 implementation-defined default.
    /// </summary>
    public bool BoundarySpaceStrip { get; set; }

    /// <summary>
    /// Optional fallback for variable resolution. When set, called when a variable is not found
    /// in any scope. Used by XSLT to trigger lazy initialization of pending global variables.
    /// Returns (true, value) if handled, (false, null) otherwise.
    /// </summary>
    public Func<QName, (bool found, object? value)>? VariableFallback { get; set; }

    /// <summary>
    /// Binds a variable in the current scope.
    /// </summary>
    public void BindVariable(QName name, object? value)
    {
        _scopes.Peek().Variables[name] = value;
    }

    /// <summary>
    /// Gets a bound variable value.
    /// </summary>
    public object? GetVariable(QName name)
    {
        foreach (var scope in _scopes)
        {
            if (scope.Variables.TryGetValue(name, out var value))
                return value;
        }
        // Try fallback (used by XSLT for lazy global variable initialization)
        if (VariableFallback != null)
        {
            var (found, fallbackValue) = VariableFallback(name);
            if (found)
                return fallbackValue;
        }
        throw new XQueryRuntimeException("XPST0008", $"Variable ${name} not bound");
    }

    /// <summary>
    /// Tries to get a bound variable value.
    /// </summary>
    public bool TryGetVariable(QName name, out object? value)
    {
        foreach (var scope in _scopes)
        {
            if (scope.Variables.TryGetValue(name, out value))
                return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Sets an external variable value to be used when <c>declare variable $name external</c>
    /// is evaluated. Call this before executing the query.
    /// </summary>
    /// <param name="name">The QName of the external variable (must match the declaration).</param>
    /// <param name="value">The value to bind.</param>
    public void SetExternalVariable(QName name, object? value)
    {
        _externalVariables[name] = value;
    }

    /// <summary>
    /// Sets an external variable by local name (assumes no namespace).
    /// Convenience overload for the common case.
    /// </summary>
    /// <param name="localName">The local name of the external variable.</param>
    /// <param name="value">The value to bind.</param>
    public void SetExternalVariable(string localName, object? value)
    {
        _externalVariables[new QName(NamespaceId.None, localName)] = value;
    }

    /// <summary>
    /// Tries to get an external variable binding. Used by <see cref="VariableDeclarationOperator"/>
    /// during execution of external variable declarations.
    /// </summary>
    internal bool TryGetExternalVariable(QName name, out object? value)
    {
        return _externalVariables.TryGetValue(name, out value);
    }

    /// <summary>
    /// Pushes a new variable scope, reusing a pooled scope if available.
    /// </summary>
    public void PushScope()
    {
        var scope = _scopePool.Count > 0 ? _scopePool.Pop() : new Scope();
        _scopes.Push(scope);
    }

    /// <summary>
    /// Pops the current variable scope and returns it to the pool.
    /// </summary>
    public void PopScope()
    {
        var scope = _scopes.Pop();
        scope.Variables.Clear();
        _scopePool.Push(scope);
    }

    /// <summary>
    /// Pushes a context item.
    /// </summary>
    public void PushContextItem(object? item, int position = 1, int size = 1)
    {
        _contextItems.Push(item);
        _positions.Push(position);
        _sizes.Push(size);
    }

    /// <summary>
    /// Pops the context item.
    /// </summary>
    public void PopContextItem()
    {
        _contextItems.Pop();
        _positions.Pop();
        _sizes.Pop();
    }

    /// <summary>
    /// Sentinel value indicating the focus is absent (XPDY0002 on access).
    /// </summary>
    public static readonly object AbsentFocus = new();

    /// <summary>
    /// Gets the current context item.
    /// </summary>
#pragma warning disable CA1065 // XQuery spec mandates XPDY0002 when focus is absent
    public object? ContextItem
    {
        get
        {
            if (_contextItems.Count == 0) return null;
            var item = _contextItems.Peek();
            if (ReferenceEquals(item, AbsentFocus))
                throw new XQueryRuntimeException("XPDY0002", "Context item is absent");
            return item;
        }
    }

    /// <summary>
    /// Gets the current position (1-based).
    /// </summary>
    public int Position
    {
        get
        {
            if (_contextItems.Count > 0 && ReferenceEquals(_contextItems.Peek(), AbsentFocus))
                throw new XQueryRuntimeException("XPDY0002", "Context position is absent");
            return _positions.Count > 0 ? _positions.Peek() : 1;
        }
    }

    /// <summary>
    /// Gets the context size.
    /// </summary>
    public int Last
    {
        get
        {
            if (_contextItems.Count > 0 && ReferenceEquals(_contextItems.Peek(), AbsentFocus))
                throw new XQueryRuntimeException("XPDY0002", "Context size is absent");
            return _sizes.Count > 0 ? _sizes.Peek() : 1;
        }
    }
#pragma warning restore CA1065

    /// <summary>
    /// Pushes a supplementary node provider that will be checked before the main provider.
    /// Used by transform copy/modify/return to make deep-copied nodes resolvable.
    /// </summary>
    public void PushNodeProvider(INodeProvider provider)
    {
        _supplementaryNodeProviders.Push(provider);
    }

    /// <summary>
    /// Pops the most recently pushed supplementary node provider.
    /// </summary>
    public void PopNodeProvider()
    {
        _supplementaryNodeProviders.Pop();
    }

    /// <summary>
    /// Loads a node by ID. Checks supplementary providers first (LIFO), then the main provider.
    /// </summary>
    public XdmNode? LoadNode(NodeId nodeId)
    {
        foreach (var provider in _supplementaryNodeProviders)
        {
            var node = provider.GetNode(nodeId);
            if (node != null) return node;
        }
        return _nodeProvider?.GetNode(nodeId);
    }

    /// <summary>
    /// Loads a node by nullable ID.
    /// </summary>
    public XdmNode? LoadNode(NodeId? nodeId)
    {
        if (!nodeId.HasValue || nodeId.Value == NodeId.None)
            return null;
        return LoadNode(nodeId.Value);
    }

    /// <summary>
    /// Checks the materialization limit and throws if exceeded.
    /// Call this when adding items to in-memory collections during query execution.
    /// </summary>
    public void CheckMaterializationLimit(int count)
    {
        if (count > Limits.MaxResultItems)
            throw new XQueryRuntimeException(
                "FOER0000",
                $"Query exceeded maximum materialization limit of {Limits.MaxResultItems} items. " +
                "Consider adding filters or increasing QueryExecutionLimits.MaxResultItems.");
    }

    /// <summary>
    /// Checks recursion depth and throws if exceeded.
    /// Call this during recursive tree traversal.
    /// </summary>
    public void CheckRecursionDepth(int depth)
    {
        if (depth > Limits.MaxRecursionDepth)
            throw new XQueryRuntimeException(
                "FOER0000",
                $"Query exceeded maximum recursion depth of {Limits.MaxRecursionDepth}. " +
                "The document may be too deeply nested or contain cycles.");
    }

    /// <summary>
    /// Enters a function call, checking the recursion limit.
    /// Must be paired with ExitFunctionCall in a try/finally block.
    /// </summary>
    public void EnterFunctionCall()
    {
        _functionCallDepth++;
        if (_functionCallDepth > Limits.MaxRecursionDepth)
            throw new XQueryRuntimeException(
                "FOER0000",
                $"Function call exceeded maximum recursion depth of {Limits.MaxRecursionDepth}. " +
                "The query may have infinite recursion or require a higher limit.");
    }

    /// <summary>
    /// Exits a function call.
    /// </summary>
    public void ExitFunctionCall()
    {
        _functionCallDepth--;
    }

    /// <summary>
    /// Computes the effective boolean value of a value.
    /// </summary>
    public static bool EffectiveBooleanValue(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s),
            Xdm.XsUntypedAtomic ua => !string.IsNullOrEmpty(ua.Value),
            Xdm.XsAnyUri uri => !string.IsNullOrEmpty(uri.Value),
            int i => i != 0,
            long l => l != 0,
            float f => f != 0 && !float.IsNaN(f),
            double d => d != 0 && !double.IsNaN(d),
            decimal dec => dec != 0,
            System.Numerics.BigInteger bi => !bi.IsZero,
            XdmNode => true,
            Xdm.TextNodeItem => true, // Lightweight text node from xsl:value-of in functions
            System.Xml.XmlNode => true,
            System.Xml.Linq.XNode => true,
            IDictionary<object, object?> => throw new XQueryRuntimeException("FORG0006",
                "Effective boolean value not defined for a map"),
            IEnumerable<object?> seq => EbvSequence(seq),
            _ => throw new XQueryRuntimeException("FORG0006",
                $"Effective boolean value not defined for type {value!.GetType().Name}")
        };
    }

    private static bool EbvSequence(IEnumerable<object?> seq)
    {
        using var enumerator = seq.GetEnumerator();
        if (!enumerator.MoveNext())
            return false; // empty sequence
        var first = enumerator.Current;
        if (first is XdmNode or Xdm.TextNodeItem)
            return true; // first item is a node → true
        if (!enumerator.MoveNext())
            return EffectiveBooleanValue(first); // single non-node item → recurse
        // 2+ items starting with non-node → FORG0006
        throw new XQueryRuntimeException("FORG0006",
            "Effective boolean value not defined for a sequence of two or more items starting with a non-node value");
    }

    /// <summary>
    /// Atomizes a value (extracts the typed value from nodes).
    /// </summary>
    public static object? Atomize(object? value) => Atomize(value, null);

    /// <summary>
    /// Instance method that atomizes using this context's node provider for string value computation.
    /// </summary>
    public object? AtomizeWithNodes(object? value) => Atomize(value, NodeProvider);

    public static object? Atomize(object? value, INodeProvider? nodeProvider)
    {
        return value switch
        {
            null => null,
            XdmElement elem => ComputeElementStringValue(elem, nodeProvider),
            XdmAttribute attr => attr.Value,
            XdmText text => text.Value,
            PhoenixmlDb.Xdm.TextNodeItem tni => tni.Value,
            XdmComment comment => comment.Value,
            XdmProcessingInstruction pi => pi.Value,
            XdmDocument doc => ComputeDocumentStringValue(doc, nodeProvider),
            // System.Xml DOM nodes (used by XSLT engine)
            System.Xml.XmlElement xmlElem => xmlElem.InnerText,
            System.Xml.XmlAttribute xmlAttr => xmlAttr.Value,
            System.Xml.XmlText xmlText => xmlText.Value,
            System.Xml.XmlComment xmlComment => xmlComment.Value,
            System.Xml.XmlProcessingInstruction xmlPi => xmlPi.Value ?? "",
            System.Xml.XmlDocument xmlDoc => xmlDoc.InnerText ?? "",
            // LINQ XML nodes (from fn:json-to-xml, fn:parse-xml, etc.)
            System.Xml.Linq.XElement linqElem => linqElem.Value,
            System.Xml.Linq.XAttribute linqAttr => linqAttr.Value,
            System.Xml.Linq.XText linqText => linqText.Value,
            System.Xml.Linq.XComment linqComment => linqComment.Value,
            System.Xml.Linq.XProcessingInstruction linqPi => linqPi.Data,
            System.Xml.Linq.XDocument linqDoc => linqDoc.Root?.Value ?? "",
            IDictionary<object, object?> => throw new PhoenixmlDb.XQuery.Functions.XQueryException("FOTY0013", "Atomization is not defined for maps"),
            List<object?> => throw new PhoenixmlDb.XQuery.Functions.XQueryException("FOTY0013", "Atomization is not defined for arrays"),
            XQueryFunction => throw new PhoenixmlDb.XQuery.Functions.XQueryException("FOTY0013", "Atomization is not defined for function items"),
            IEnumerable<object?> seq => seq.Select(v => Atomize(v, nodeProvider)).ToArray(),
            _ => value
        };
    }

    /// <summary>
    /// Computes the string value of an element by walking descendant text nodes.
    /// Per XQuery spec, the string value of an element is the concatenation of all
    /// descendant text nodes in document order.
    /// </summary>
    internal static string ComputeElementStringValue(XdmElement elem, INodeProvider? nodeProvider)
    {
        // If pre-computed, use it
        var precomputed = elem.StringValue;
        if (!string.IsNullOrEmpty(precomputed))
            return precomputed;

        // Walk descendant text nodes via the node provider
        if (nodeProvider == null || elem.Children.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        CollectTextDescendants(elem, nodeProvider, sb);
        return sb.ToString();
    }

    internal static string ComputeDocumentStringValue(XdmDocument doc, INodeProvider? nodeProvider)
    {
        var precomputed = doc.StringValue;
        if (!string.IsNullOrEmpty(precomputed))
            return precomputed;

        if (nodeProvider == null || doc.Children.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        foreach (var childId in doc.Children)
        {
            var child = nodeProvider.GetNode(childId);
            if (child is XdmText text)
                sb.Append(text.Value);
            else if (child is XdmElement childElem)
                CollectTextDescendants(childElem, nodeProvider, sb);
        }
        return sb.ToString();
    }

    private static void CollectTextDescendants(XdmElement elem, INodeProvider nodeProvider, System.Text.StringBuilder sb)
    {
        foreach (var childId in elem.Children)
        {
            var child = nodeProvider.GetNode(childId);
            if (child is XdmText text)
                sb.Append(text.Value);
            else if (child is XdmElement childElem)
                CollectTextDescendants(childElem, nodeProvider, sb);
        }
    }

    /// <summary>
    /// Atomizes a value preserving xs:untypedAtomic type for untyped node content.
    /// Use this in binary operators to distinguish xs:string (from literals) from
    /// xs:untypedAtomic (from untyped node content) for XPath 2.0+ type checking.
    /// </summary>
    public static object? AtomizeTyped(object? value)
    {
        return value switch
        {
            null => null,
            XdmElement elem => new Xdm.XsUntypedAtomic(elem.StringValue),
            XdmAttribute attr => new Xdm.XsUntypedAtomic(attr.Value),
            XdmText text => new Xdm.XsUntypedAtomic(text.Value),
            PhoenixmlDb.Xdm.TextNodeItem tni => new Xdm.XsUntypedAtomic(tni.Value),
            XdmComment comment => comment.Value,
            XdmProcessingInstruction pi => pi.Value,
            XdmDocument doc => new Xdm.XsUntypedAtomic(doc.StringValue),
            // System.Xml DOM nodes (used by XSLT engine)
            System.Xml.XmlElement xmlElem => new Xdm.XsUntypedAtomic(xmlElem.InnerText ?? ""),
            System.Xml.XmlAttribute xmlAttr => new Xdm.XsUntypedAtomic(xmlAttr.Value),
            System.Xml.XmlText xmlText => new Xdm.XsUntypedAtomic(xmlText.Value ?? ""),
            System.Xml.XmlComment xmlComment => xmlComment.Value ?? "",
            System.Xml.XmlProcessingInstruction xmlPi => xmlPi.Value ?? "",
            System.Xml.XmlDocument xmlDoc => new Xdm.XsUntypedAtomic(xmlDoc.InnerText ?? ""),
            // LINQ XML nodes (from fn:json-to-xml, fn:parse-xml, etc.)
            System.Xml.Linq.XElement linqElem => new Xdm.XsUntypedAtomic(linqElem.Value),
            System.Xml.Linq.XAttribute linqAttr => new Xdm.XsUntypedAtomic(linqAttr.Value),
            System.Xml.Linq.XText linqText => new Xdm.XsUntypedAtomic(linqText.Value),
            System.Xml.Linq.XComment linqComment => linqComment.Value,
            System.Xml.Linq.XProcessingInstruction linqPi => linqPi.Data,
            System.Xml.Linq.XDocument linqDoc => new Xdm.XsUntypedAtomic(linqDoc.Root?.Value ?? ""),
            IDictionary<object, object?> => throw new PhoenixmlDb.XQuery.Functions.XQueryException("FOTY0013", "Atomization is not defined for maps"),
            List<object?> => throw new PhoenixmlDb.XQuery.Functions.XQueryException("FOTY0013", "Atomization is not defined for arrays"),
            XQueryFunction => throw new PhoenixmlDb.XQuery.Functions.XQueryException("FOTY0013", "Atomization is not defined for function items"),
            IEnumerable<object?> seq => seq.Select(AtomizeTyped).ToArray(),
            _ => value
        };
    }

    /// <summary>
    /// Atomizes a value to a single item. For sequences, returns the first item.
    /// Use this for functions expecting a single atomic argument (e.g., ceiling, floor).
    /// </summary>
    public static object? AtomizeSingle(object? value)
    {
        var atomized = Atomize(value);
        if (atomized is object?[] arr)
            return arr.Length > 0 ? arr[0] : null;
        return atomized;
    }

    /// <summary>
    /// Safely converts a value to double, atomizing XDM nodes first.
    /// </summary>
    public static double ToDouble(object? value)
    {
        var atomized = AtomizeSingle(value);
        if (atomized is null) return double.NaN;
        if (atomized is double d) return d;
        if (atomized is float f) return f;
        if (atomized is int i) return i;
        if (atomized is long l) return l;
        if (atomized is decimal m) return (double)m;
        if (atomized is System.Numerics.BigInteger bi) return (double)bi;
        if (atomized is string s)
        {
            s = s.Trim();
            if (s is "INF" or "+INF") return double.PositiveInfinity;
            if (s is "-INF") return double.NegativeInfinity;
            return double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : double.NaN;
        }
        return Convert.ToDouble(atomized);
    }

    /// <summary>
    /// Safely converts a value to long, atomizing XDM nodes first.
    /// </summary>
    public static long ToLong(object? value)
    {
        var atomized = AtomizeSingle(value);
        if (atomized is null) return 0;
        if (atomized is long l) return l;
        if (atomized is int i) return i;
        if (atomized is double d) return (long)Math.Round(d);
        if (atomized is decimal m) return (long)Math.Round(m);
        if (atomized is System.Numerics.BigInteger bi) return (long)bi;
        if (atomized is string s)
        {
            return long.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0;
        }
        return Convert.ToInt64(atomized);
    }

    /// <summary>
    /// Safely converts a value to int, atomizing XDM nodes first.
    /// </summary>
    public static int ToInt(object? value)
    {
        return (int)ToLong(value);
    }

    /// <summary>
    /// Safely converts a value to string, atomizing XDM nodes first.
    /// </summary>
    public static string ToString(object? value)
    {
        var atomized = AtomizeSingle(value);
        if (atomized is object?[] arr)
            return arr.Length > 0 ? XQuery.Functions.ConcatFunction.XQueryStringValue(arr[0]) : "";
        return XQuery.Functions.ConcatFunction.XQueryStringValue(atomized);
    }

    public void Dispose()
    {
        _scopes.Clear();
        _contextItems.Clear();
        _positions.Clear();
        _sizes.Clear();
    }

    /// <summary>
    /// Captures a snapshot of all currently in-scope variables for use in closures.
    /// </summary>
    public Dictionary<QName, object?> CaptureVariables()
    {
        var snapshot = new Dictionary<QName, object?>();
        // Walk scopes from bottom to top so inner scopes shadow outer
        foreach (var scope in _scopes.Reverse())
        {
            foreach (var (name, value) in scope.Variables)
                snapshot[name] = value;
        }
        return snapshot;
    }

    private sealed class Scope
    {
        public Dictionary<QName, object?> Variables { get; } = new();
    }
}

/// <summary>
/// Exception thrown when an XQuery expression fails during execution (as opposed to during parsing or static analysis).
/// </summary>
/// <remarks>
/// <para>
/// Runtime exceptions correspond to XQuery dynamic errors defined in the XQuery 3.1 specification.
/// The <see cref="ErrorCode"/> property contains a standard XQuery error code (e.g., <c>"XPDY0002"</c>,
/// <c>"FOER0000"</c>, <c>"FORG0006"</c>) that identifies the error category.
/// </para>
/// <para>
/// Common error codes:
/// <list type="bullet">
///   <item><description><c>XPDY0002</c> — Context item is undefined (e.g., using <c>.</c> without a context).</description></item>
///   <item><description><c>XPST0008</c> — Unbound variable reference.</description></item>
///   <item><description><c>FORG0006</c> — Invalid argument type (e.g., effective boolean value of a map).</description></item>
///   <item><description><c>FOER0000</c> — Unidentified error, including execution limit violations.</description></item>
/// </list>
/// </para>
/// </remarks>
/// <seealso cref="Functions.XQueryException"/>
/// <seealso cref="Parser.XQueryParseException"/>
public class XQueryRuntimeException : Exception
{
    /// <summary>
    /// The XQuery error code (e.g., <c>"XPDY0002"</c>, <c>"FOER0000"</c>) identifying the error category
    /// as defined by the XQuery 3.1 and XPath 3.1 specifications.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Creates a new runtime exception with the specified XQuery error code and message.
    /// </summary>
    /// <param name="errorCode">A standard XQuery error code (e.g., <c>"XPDY0002"</c>).</param>
    /// <param name="message">A human-readable description of the error.</param>
    public XQueryRuntimeException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a new runtime exception with the specified error code, message, and inner exception.
    /// </summary>
    /// <param name="errorCode">A standard XQuery error code.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public XQueryRuntimeException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Adapts an <see cref="INodeProvider"/> and optional namespace resolver into <see cref="INodeStore"/>.
/// Used by <see cref="QueryExecutionContext"/> to expose tree navigation to shared functions.
/// </summary>
internal sealed class NodeStoreAdapter : INodeStore
{
    private readonly INodeProvider _provider;
    private readonly Func<NamespaceId, string?>? _namespaceResolver;

    public NodeStoreAdapter(INodeProvider provider, Func<NamespaceId, string?>? namespaceResolver)
    {
        _provider = provider;
        _namespaceResolver = namespaceResolver;
    }

    public XdmNode? GetNode(NodeId nodeId) => _provider.GetNode(nodeId);

    public string? GetNamespaceUri(NamespaceId id)
    {
        if (id == NamespaceId.None) return null;
        return _namespaceResolver?.Invoke(id);
    }

    public IEnumerable<XdmAttribute> GetAttributes(XdmElement element)
    {
        foreach (var attrId in element.Attributes)
        {
            if (_provider.GetNode(attrId) is XdmAttribute attr)
                yield return attr;
        }
    }
}
