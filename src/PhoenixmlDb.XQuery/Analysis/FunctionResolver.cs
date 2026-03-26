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
        }

        // Walk arguments
        foreach (var arg in expr.Arguments)
            Walk(arg);

        return null;
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
