using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Resolves function calls to their implementations.
/// </summary>
public sealed class FunctionResolver : XQueryExpressionWalker
{
    private readonly FunctionLibrary _library;
    private readonly List<AnalysisError> _errors = [];

    public FunctionResolver(FunctionLibrary library)
    {
        _library = library;
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
        var function = _library.Resolve(expr.Name, expr.Arguments.Count);

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
