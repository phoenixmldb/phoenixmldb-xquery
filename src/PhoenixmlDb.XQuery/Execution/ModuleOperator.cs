using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Module operator: executes declarations then body.
/// </summary>
public sealed class ModuleOperator : PhysicalOperator
{
    public required IReadOnlyList<PhysicalOperator> Declarations { get; init; }
    public required PhysicalOperator Body { get; init; }
    public Dictionary<string, string>? NamespaceBindings { get; init; }
    public Dictionary<string, Analysis.DecimalFormatProperties>? DecimalFormats { get; init; }
    public string? DefaultCollation { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Set namespace bindings from prolog for runtime use by computed constructors
        if (NamespaceBindings != null)
        {
            context.PrefixNamespaceBindings = NamespaceBindings;
            // Save a snapshot of the prolog bindings so ElementConstructorOperator can
            // distinguish prolog-level bindings from those added by enclosing constructors.
            context.PrologNamespaceBindings = new Dictionary<string, string>(NamespaceBindings);
        }

        // Set default collation from prolog declaration
        if (DefaultCollation != null)
            context.DefaultCollation = DefaultCollation;

        // Set decimal-format properties from prolog for format-number()
        if (DecimalFormats != null)
        {
            foreach (var (name, props) in DecimalFormats)
                context.DecimalFormats[name] = props;
        }

        // Register function declarations first (they don't depend on variable values at registration time)
        foreach (var decl in Declarations.Where(d => d is FunctionDeclarationOperator))
        {
            await foreach (var _ in decl.ExecuteAsync(context))
            {
            }
        }

        // Collect variable and context item declarations for lazy evaluation.
        // XQuery 3.1 §2.1.1: "All variable declarations [...] are visible throughout the module"
        // Forward references between variables require lazy/on-demand initialization.
        var pendingVarDecls = new Dictionary<QName, VariableDeclarationOperator>(QNameComparer.Instance);
        var otherDecls = new List<PhysicalOperator>();
        // Multiple context-item declarations are valid when imported modules each
        // declare their own — the main module's binds the value (or accepts the
        // external one) and each imported module's then type-checks it
        // (contextDecl-050/051/054 — XPTY0004 on conflicting types).
        var contextItemDecls = new List<ContextItemDeclarationOperator>();

        foreach (var decl in Declarations)
        {
            if (decl is VariableDeclarationOperator varDecl)
                pendingVarDecls[varDecl.VariableName] = varDecl;
            else if (decl is ContextItemDeclarationOperator ctxDecl)
                contextItemDecls.Add(ctxDecl);
            else if (decl is not FunctionDeclarationOperator)
                otherDecls.Add(decl);
        }

        // Set of variables currently being initialized (cycle detection)
        var initializing = new HashSet<QName>(QNameComparer.Instance);
        var initialized = new HashSet<QName>(QNameComparer.Instance);

        // Lazy initializer: when a variable is referenced before it's been evaluated,
        // evaluate it on demand (supporting forward references).
        var previousFallback = context.VariableFallback;
        context.VariableFallback = (name) =>
        {
            if (pendingVarDecls.TryGetValue(name, out var pending) && !initialized.Contains(name))
            {
                if (initializing.Contains(name))
                    throw new XQueryRuntimeException("XQDY0054",
                        $"Circular dependency detected initializing variable ${name}");

                InitializeVariableSync(pending, context, initializing, initialized);
                // After initialization, the variable should be bound
                try
                {
                    var val = context.GetVariable(name);
                    return (true, val);
                }
                catch
                {
                    return (false, null);
                }
            }
            return previousFallback?.Invoke(name) ?? (false, null);
        };

        // Initialize context item first if possible — but if its initializer references
        // a variable, the lazy fallback will handle the forward reference. Multiple
        // declarations run in order; the first one with a default value (or external
        // binding) sets the item, subsequent ones type-check the existing value.
        foreach (var contextItemDecl in contextItemDecls)
        {
            await foreach (var _ in contextItemDecl.ExecuteAsync(context))
            {
            }
        }

        // Evaluate all variable declarations (lazy fallback handles forward references)
        foreach (var (varName, varDecl) in pendingVarDecls)
        {
            if (!initialized.Contains(varName))
            {
                await InitializeVariableAsync(varDecl, context, initializing, initialized);
            }
        }

        // Restore previous fallback
        context.VariableFallback = previousFallback;

        // Process remaining non-variable, non-function, non-context-item declarations
        foreach (var decl in otherDecls)
        {
            await foreach (var _ in decl.ExecuteAsync(context))
            {
            }
        }

        // Execute the body
        await foreach (var item in Body.ExecuteAsync(context))
            yield return item;
    }

    private static async Task InitializeVariableAsync(
        VariableDeclarationOperator varDecl,
        QueryExecutionContext context,
        HashSet<QName> initializing,
        HashSet<QName> initialized)
    {
        initializing.Add(varDecl.VariableName);
        await foreach (var _ in varDecl.ExecuteAsync(context))
        {
        }
        initializing.Remove(varDecl.VariableName);
        initialized.Add(varDecl.VariableName);
    }

    private static void InitializeVariableSync(
        VariableDeclarationOperator varDecl,
        QueryExecutionContext context,
        HashSet<QName> initializing,
        HashSet<QName> initialized)
    {
        initializing.Add(varDecl.VariableName);
        var enumerator = varDecl.ExecuteAsync(context).GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        initializing.Remove(varDecl.VariableName);
        initialized.Add(varDecl.VariableName);
    }

    /// <summary>
    /// QName equality comparer that matches by namespace + local name (or prefix + local name if no namespace).
    /// </summary>
    private sealed class QNameComparer : IEqualityComparer<QName>
    {
        public static readonly QNameComparer Instance = new();

        public bool Equals(QName x, QName y)
        {
            if (x.LocalName != y.LocalName) return false;
            // Compare by namespace if available, otherwise by prefix
            var xNs = x.ExpandedNamespace;
            var yNs = y.ExpandedNamespace;
            if (xNs != null || yNs != null)
                return xNs == yNs;
            return x.Prefix == y.Prefix;
        }

        public int GetHashCode(QName obj)
        {
            var ns = obj.ExpandedNamespace;
            return HashCode.Combine(obj.LocalName, ns ?? obj.Prefix ?? "");
        }
    }
}
