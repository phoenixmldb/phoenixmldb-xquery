using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Resolves function calls to their implementations.
/// </summary>
public sealed class FunctionResolver : XQueryExpressionWalker
{
    private readonly FunctionLibrary _library;
    private readonly NamespaceContext? _namespaces;
    private readonly List<AnalysisError> _errors = [];
    /// <summary>
    /// When > 0, we're inside an imported module's function or variable body where
    /// private module functions should be accessible. When 0, private functions
    /// from imported modules should not be accessible.
    /// </summary>
    private int _insideImportedModuleCodeDepth;

    /// <summary>
    /// Stack of namespace URIs for the imported-module bodies we're currently
    /// analyzing. Top entry = the module declaring the function/variable whose
    /// body we're inside. Used to gate private-function calls: a call to
    /// <c>mod:f()</c> from another module's body is XPST0017 even though the
    /// other body is itself inside an imported module — privacy is module-local,
    /// not "any-imported-module-may-call-any-other-imported-module's-privates."
    /// Empty when analyzing the main query.
    /// </summary>
    private readonly Stack<string> _currentModuleNamespaceStack = new();

    private readonly HashSet<string>? _importedModuleNamespaces;

    public FunctionResolver(FunctionLibrary library, NamespaceContext? namespaces = null,
        IEnumerable<string>? importedModuleNamespaces = null)
    {
        _library = library;
        _namespaces = namespaces;
        _importedModuleNamespaces = importedModuleNamespaces != null
            ? new HashSet<string>(importedModuleNamespaces)
            : null;
    }

    /// <summary>
    /// Resolves all function calls in the expression.
    /// </summary>
    public XQueryExpression Resolve(XQueryExpression expression, List<AnalysisError> errors)
    {
        _errors.Clear();
        _insideImportedModuleCodeDepth = 0;
        Walk(expression);
        errors.AddRange(_errors);
        return expression;
    }

    public override object? VisitFunctionDeclaration(FunctionDeclarationExpression expr)
    {
        // If this function is from an imported module, its body can access private
        // functions from the same module. Detect by checking if the function's
        // namespace matches a known imported module namespace, or if ModuleBaseUri is set.
        bool isImportedModule = expr.ModuleBaseUri != null || IsFromImportedModule(expr.Name);
        var declaringNs = ResolveDeclaringNamespace(expr.Name);
        if (isImportedModule)
        {
            _insideImportedModuleCodeDepth++;
            _currentModuleNamespaceStack.Push(declaringNs ?? "");
        }
        try
        {
            Walk(expr.Body);
        }
        finally
        {
            if (isImportedModule)
            {
                _currentModuleNamespaceStack.Pop();
                _insideImportedModuleCodeDepth--;
            }
        }
        return null;
    }

    /// <summary>
    /// True when the current analysis context is inside the same module that
    /// declared <paramref name="calleeName"/>. Private functions are only
    /// callable from their own module — not from the main query, not from
    /// other imported modules.
    /// </summary>
    private bool PrivateCallAllowed(Core.QName calleeName)
    {
        if (_currentModuleNamespaceStack.Count == 0) return false; // main query
        var currentModule = _currentModuleNamespaceStack.Peek();
        var calleeNs = ResolveDeclaringNamespace(calleeName);
        return calleeNs != null && calleeNs == currentModule;
    }

    private string? ResolveDeclaringNamespace(Core.QName name)
    {
        if (!string.IsNullOrEmpty(name.ExpandedNamespace)) return name.ExpandedNamespace;
        if (_namespaces != null && name.Prefix != null)
            return _namespaces.ResolvePrefix(name.Prefix);
        if (_namespaces != null && name.Namespace != Core.NamespaceId.None)
            return _namespaces.GetUri(name.Namespace);
        return null;
    }

    private bool IsFromImportedModule(Core.QName name)
    {
        if (_importedModuleNamespaces == null) return false;
        var ns = name.ExpandedNamespace;
        if (ns == null && _namespaces != null && name.Prefix != null)
            ns = _namespaces.ResolvePrefix(name.Prefix);
        if (ns == null && _namespaces != null && name.Namespace != Core.NamespaceId.None)
            ns = _namespaces.GetUri(name.Namespace);
        return ns != null && _importedModuleNamespaces.Contains(ns);
    }

    public override object? VisitVariableDeclaration(VariableDeclarationExpression expr)
    {
        // Variable initializers from imported modules can access private functions.
        // Detect imported module variables by checking if their namespace matches
        // a known imported module namespace.
        bool isImportedModule = false;
        if (_importedModuleNamespaces != null)
        {
            var ns = expr.Name.ExpandedNamespace;
            if (ns == null && _namespaces != null && expr.Name.Prefix != null)
                ns = _namespaces.ResolvePrefix(expr.Name.Prefix);
            if (ns != null)
                isImportedModule = _importedModuleNamespaces.Contains(ns);
        }

        var declaringNs = ResolveDeclaringNamespace(expr.Name);
        if (isImportedModule)
        {
            _insideImportedModuleCodeDepth++;
            _currentModuleNamespaceStack.Push(declaringNs ?? "");
        }
        try
        {
            if (expr.Value != null)
                Walk(expr.Value);
        }
        finally
        {
            if (isImportedModule)
            {
                _currentModuleNamespaceStack.Pop();
                _insideImportedModuleCodeDepth--;
            }
        }
        return null;
    }

    public override object? VisitFunctionCallExpression(FunctionCallExpression expr)
    {
        // Resolve namespace prefix on function name before lookup
        var resolvedName = expr.Name;
        if (_namespaces != null && resolvedName.Namespace == Core.NamespaceId.None)
        {
            // Q{uri}local — ExpandedNamespace already set by parser, just intern
            if (!string.IsNullOrEmpty(resolvedName.ExpandedNamespace))
            {
                var nsId = _namespaces.GetOrCreateId(resolvedName.ExpandedNamespace);
                resolvedName = new Core.QName(nsId, resolvedName.LocalName) { RuntimeNamespace = resolvedName.ExpandedNamespace };
                expr.Name = resolvedName;
            }
            else if (resolvedName.Prefix != null)
            {
                var uri = _namespaces.ResolvePrefix(resolvedName.Prefix);
                if (uri != null)
                {
                    var nsId = _namespaces.GetOrCreateId(uri);
                    resolvedName = new Core.QName(nsId, resolvedName.LocalName, resolvedName.Prefix);
                    expr.Name = resolvedName;
                }
            }
            else
            {
                // Unprefixed function call: apply `declare default function namespace`
                // if set; otherwise fall back to the fn: namespace.
                var defaultUri = _namespaces.ResolvePrefix("##default-function");
                if (!string.IsNullOrEmpty(defaultUri))
                {
                    var nsId = _namespaces.GetOrCreateId(defaultUri);
                    resolvedName = new Core.QName(nsId, resolvedName.LocalName) { RuntimeNamespace = defaultUri };
                    expr.Name = resolvedName;
                }
            }
        }

        var function = _library.Resolve(resolvedName, expr.Arguments.Count);

        // Module-private functions are accessible only from within the SAME module
        // that declared them. Calling from main (no current module) or from a
        // different module's body is XPST0017.
        if (function is DeclaredFunctionPlaceholder placeholder && placeholder.IsModulePrivate
            && !PrivateCallAllowed(resolvedName))
        {
            _errors.Add(new AnalysisError(
                XQueryErrorCodes.XPST0017,
                $"Function {expr.Name.LocalName}#{expr.Arguments.Count} is private and not accessible",
                expr.Location));
        }
        else if (function == null)
        {
            _errors.Add(new AnalysisError(
                XQueryErrorCodes.XPST0017,
                $"Unknown function: {expr.Name.LocalName}#{expr.Arguments.Count}",
                expr.Location));
        }
        else
        {
            expr.ResolvedFunction = function;

            // XPath 4.0: reorder keyword arguments to positional based on parameter names
            if (expr.Arguments.Any(a => a is KeywordArgument))
                ReorderKeywordArguments(expr, function);
        }

        // Walk arguments (after reordering so walkers see final positions)
        foreach (var arg in expr.Arguments)
            Walk(arg);

        return null;
    }

    /// <summary>
    /// Rewrites a function call's argument list by mapping keyword arguments to their
    /// positional slots based on the resolved function's parameter definitions.
    /// Positional arguments must come before keyword arguments (XPST0003).
    /// </summary>
    private void ReorderKeywordArguments(FunctionCallExpression expr, XQueryFunction function)
    {
        var parameters = function.Parameters;
        var positionalArgs = new List<XQueryExpression>();
        var keywordArgs = new Dictionary<string, (XQueryExpression Value, SourceLocation? Location)>();
        var seenKeyword = false;

        foreach (var arg in expr.Arguments)
        {
            if (arg is KeywordArgument kw)
            {
                seenKeyword = true;
                if (keywordArgs.ContainsKey(kw.Name))
                {
                    _errors.Add(new AnalysisError(
                        XQueryErrorCodes.XPST0003,
                        $"Duplicate keyword argument: {kw.Name}",
                        kw.Location));
                    return;
                }
                keywordArgs[kw.Name] = (kw.Value, kw.Location);
            }
            else
            {
                if (seenKeyword)
                {
                    _errors.Add(new AnalysisError(
                        XQueryErrorCodes.XPST0003,
                        "Positional argument cannot follow keyword argument",
                        arg.Location));
                    return;
                }
                positionalArgs.Add(arg);
            }
        }

        // Build the reordered argument list
        var reordered = new XQueryExpression[parameters.Count];

        // Place positional arguments first
        for (var i = 0; i < positionalArgs.Count; i++)
        {
            if (i >= parameters.Count)
            {
                _errors.Add(new AnalysisError(
                    XQueryErrorCodes.XPST0017,
                    $"Too many arguments for {expr.Name.LocalName}#{parameters.Count}",
                    expr.Location));
                return;
            }
            reordered[i] = positionalArgs[i];
        }

        // Place keyword arguments by matching parameter names
        foreach (var (name, (value, location)) in keywordArgs)
        {
            var paramIndex = -1;
            for (var i = 0; i < parameters.Count; i++)
            {
                if (parameters[i].Name.LocalName == name)
                {
                    paramIndex = i;
                    break;
                }
            }

            if (paramIndex < 0)
            {
                _errors.Add(new AnalysisError(
                    XQueryErrorCodes.XPST0017,
                    $"Unknown parameter name '{name}' for function {expr.Name.LocalName}",
                    location));
                return;
            }

            if (reordered[paramIndex] != null)
            {
                _errors.Add(new AnalysisError(
                    XQueryErrorCodes.XPST0003,
                    $"Parameter '{name}' is already supplied by a positional argument",
                    location));
                return;
            }

            reordered[paramIndex] = value;
        }

        expr.Arguments = reordered.ToList();
    }

    public override object? VisitNamedFunctionRef(NamedFunctionRef expr)
    {
        // Resolve namespace prefix (or default function namespace for unprefixed refs)
        // so that e.g. `date#1` honors `declare default function namespace "...XMLSchema"`.
        var resolvedName = expr.Name;
        if (_namespaces != null && resolvedName.Namespace == Core.NamespaceId.None)
        {
            // Q{uri}local — ExpandedNamespace already set by parser
            if (!string.IsNullOrEmpty(resolvedName.ExpandedNamespace))
            {
                var nsId = _namespaces.GetOrCreateId(resolvedName.ExpandedNamespace);
                resolvedName = new Core.QName(nsId, resolvedName.LocalName) { RuntimeNamespace = resolvedName.ExpandedNamespace };
                expr.Name = resolvedName;
            }
            else if (resolvedName.Prefix != null)
            {
                var uri = _namespaces.ResolvePrefix(resolvedName.Prefix);
                if (uri != null)
                {
                    var nsId = _namespaces.GetOrCreateId(uri);
                    resolvedName = new Core.QName(nsId, resolvedName.LocalName, resolvedName.Prefix);
                    expr.Name = resolvedName;
                }
            }
            else
            {
                var defaultUri = _namespaces.ResolvePrefix("##default-function");
                if (defaultUri != null)
                {
                    var nsId = _namespaces.GetOrCreateId(defaultUri);
                    resolvedName = new Core.QName(nsId, resolvedName.LocalName) { RuntimeNamespace = defaultUri };
                    expr.Name = resolvedName;
                }
            }
        }

        var function = _library.Resolve(resolvedName, expr.Arity);

        if (function is DeclaredFunctionPlaceholder placeholder && placeholder.IsModulePrivate
            && !PrivateCallAllowed(resolvedName))
        {
            _errors.Add(new AnalysisError(
                XQueryErrorCodes.XPST0017,
                $"Function {expr.Name.LocalName}#{expr.Arity} is private and not accessible",
                expr.Location));
        }
        else if (function == null)
        {
            _errors.Add(new AnalysisError(
                XQueryErrorCodes.XPST0017,
                $"Unknown function: {expr.Name.LocalName}#{expr.Arity}",
                expr.Location));
        }

        return null;
    }
}
