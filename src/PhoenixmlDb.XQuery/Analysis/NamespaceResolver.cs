using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Resolves namespace prefixes in the AST.
/// </summary>
public sealed class NamespaceResolver : XQueryExpressionRewriter
{
    private readonly NamespaceContext _namespaces;
    private readonly List<AnalysisError> _errors = [];

    public NamespaceResolver(NamespaceContext namespaces)
    {
        _namespaces = namespaces;
    }

    /// <summary>
    /// Resolves all namespace prefixes in the expression.
    /// </summary>
    public XQueryExpression Resolve(XQueryExpression expression, List<AnalysisError> errors)
    {
        _errors.Clear();
        var result = Rewrite(expression);
        errors.AddRange(_errors);
        return result;
    }

    public override XQueryExpression VisitFunctionCallExpression(FunctionCallExpression expr)
    {
        // Resolve function name namespace
        var name = expr.Name;
        if (name.Prefix != null)
        {
            var uri = _namespaces.ResolvePrefix(name.Prefix);
            if (uri == null)
            {
                _errors.Add(new AnalysisError(
                    XQueryErrorCodes.XPST0081,
                    $"Unbound namespace prefix: {name.Prefix}",
                    expr.Location));
            }
            else
            {
                // Set the resolved namespace ID on the QName so that
                // FunctionResolver can look up in the correct namespace.
                var nsId = _namespaces.GetOrCreateId(uri);
                var resolvedExpr = new FunctionCallExpression
                {
                    Name = new QName(nsId, name.LocalName, name.Prefix),
                    Arguments = expr.Arguments,
                    Location = expr.Location
                };
                return base.VisitFunctionCallExpression(resolvedExpr);
            }
        }

        return base.VisitFunctionCallExpression(expr);
    }

    public override XQueryExpression VisitFunctionDeclaration(FunctionDeclarationExpression expr)
    {
        // Resolve function declaration name namespace so it matches resolved call sites
        var name = expr.Name;
        if (name.Prefix != null)
        {
            var uri = _namespaces.ResolvePrefix(name.Prefix);
            if (uri == null)
            {
                _errors.Add(new AnalysisError(
                    XQueryErrorCodes.XPST0081,
                    $"Unbound namespace prefix: {name.Prefix}",
                    expr.Location));
            }
            else
            {
                var nsId = _namespaces.GetOrCreateId(uri);
                var resolvedExpr = new FunctionDeclarationExpression
                {
                    Name = new QName(nsId, name.LocalName, name.Prefix),
                    Parameters = expr.Parameters,
                    ReturnType = expr.ReturnType,
                    Body = expr.Body,
                    Location = expr.Location
                };
                return base.VisitFunctionDeclaration(resolvedExpr);
            }
        }

        return base.VisitFunctionDeclaration(expr);
    }

    public override XQueryExpression VisitVariableReference(VariableReference expr)
    {
        // Resolve variable name namespace (if prefixed)
        var name = expr.Name;
        if (name.Prefix != null)
        {
            var uri = _namespaces.ResolvePrefix(name.Prefix);
            if (uri == null)
            {
                _errors.Add(new AnalysisError(
                    XQueryErrorCodes.XPST0081,
                    $"Unbound namespace prefix: {name.Prefix}",
                    expr.Location));
            }
        }

        return expr;
    }

    public override XQueryExpression VisitStepExpression(StepExpression expr)
    {
        // Resolve namespace in name test
        if (expr.NodeTest is NameTest nameTest)
        {
            if (nameTest.Prefix != null)
            {
                var uri = _namespaces.ResolvePrefix(nameTest.Prefix);
                if (uri == null)
                {
                    _errors.Add(new AnalysisError(
                        XQueryErrorCodes.XPST0081,
                        $"Unbound namespace prefix: {nameTest.Prefix}",
                        expr.Location));
                }
                else
                {
                    // Set the resolved namespace
                    nameTest.ResolvedNamespace = _namespaces.GetOrCreateId(uri);
                }
            }
            else if (nameTest.NamespaceUri != null && !nameTest.IsNamespaceWildcard && !nameTest.ResolvedNamespace.HasValue)
            {
                // Q{uri}* or Q{uri}name — resolve EQName namespace URI to ID
                nameTest.ResolvedNamespace = string.IsNullOrEmpty(nameTest.NamespaceUri)
                    ? NamespaceId.None
                    : _namespaces.GetOrCreateId(nameTest.NamespaceUri);
            }
        }

        // Process predicates
        var predicates = new List<XQueryExpression>();
        var changed = false;
        foreach (var pred in expr.Predicates)
        {
            var newPred = Rewrite(pred);
            predicates.Add(newPred);
            if (newPred != pred) changed = true;
        }

        if (!changed) return expr;

        return new StepExpression
        {
            Axis = expr.Axis,
            NodeTest = expr.NodeTest,
            Predicates = predicates,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitElementConstructor(ElementConstructor expr)
    {
        // Resolve element name namespace
        var name = expr.Name;
        if (name.Prefix != null)
        {
            var uri = _namespaces.ResolvePrefix(name.Prefix);
            if (uri == null)
            {
                _errors.Add(new AnalysisError(
                    XQueryErrorCodes.XPST0081,
                    $"Unbound namespace prefix: {name.Prefix}",
                    expr.Location));
            }
        }

        return base.VisitElementConstructor(expr);
    }

    public override XQueryExpression VisitAttributeConstructor(AttributeConstructor expr)
    {
        // Resolve attribute name namespace
        var name = expr.Name;
        if (name.Prefix != null)
        {
            var uri = _namespaces.ResolvePrefix(name.Prefix);
            if (uri == null)
            {
                _errors.Add(new AnalysisError(
                    XQueryErrorCodes.XPST0081,
                    $"Unbound namespace prefix: {name.Prefix}",
                    expr.Location));
            }
        }

        return base.VisitAttributeConstructor(expr);
    }

    public override XQueryExpression VisitCastExpression(CastExpression expr)
    {
        // Resolve type name namespace
        ResolveSequenceType(expr.TargetType, expr.Location);
        return base.VisitCastExpression(expr);
    }

    public override XQueryExpression VisitInstanceOfExpression(InstanceOfExpression expr)
    {
        ResolveSequenceType(expr.TargetType, expr.Location);
        return base.VisitInstanceOfExpression(expr);
    }

    public override XQueryExpression VisitTreatExpression(TreatExpression expr)
    {
        ResolveSequenceType(expr.TargetType, expr.Location);
        return base.VisitTreatExpression(expr);
    }

    private void ResolveSequenceType(XdmSequenceType type, SourceLocation? location)
    {
        // ItemType may need namespace resolution for atomic types
        // This is simplified - full implementation would check ItemType for type name
    }
}
