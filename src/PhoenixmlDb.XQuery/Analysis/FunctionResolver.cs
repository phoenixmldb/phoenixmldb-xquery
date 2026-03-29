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

    public FunctionResolver(FunctionLibrary library, NamespaceContext? namespaces = null)
    {
        _library = library;
        _namespaces = namespaces;
    }

    /// <summary>
    /// Resolves all function calls in the expression.
    /// </summary>
    public XQueryExpression Resolve(XQueryExpression expression, List<AnalysisError> errors)
    {
        _errors.Clear();
        Walk(expression);
        errors.AddRange(_errors);
        return expression;
    }

    public override object? VisitFunctionCallExpression(FunctionCallExpression expr)
    {
        // Resolve namespace prefix on function name before lookup
        var resolvedName = expr.Name;
        if (_namespaces != null && resolvedName.Prefix != null && resolvedName.Namespace == Core.NamespaceId.None)
        {
            var uri = _namespaces.ResolvePrefix(resolvedName.Prefix);
            if (uri != null)
            {
                var nsId = _namespaces.GetOrCreateId(uri);
                resolvedName = new Core.QName(nsId, resolvedName.LocalName, resolvedName.Prefix);
                expr.Name = resolvedName;
            }
        }

        var function = _library.Resolve(resolvedName, expr.Arguments.Count);

        if (function == null)
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
        var function = _library.Resolve(expr.Name, expr.Arity);

        if (function == null)
        {
            _errors.Add(new AnalysisError(
                XQueryErrorCodes.XPST0017,
                $"Unknown function: {expr.Name.LocalName}#{expr.Arity}",
                expr.Location));
        }

        return null;
    }
}
