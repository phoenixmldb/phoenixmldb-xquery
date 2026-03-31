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
                    nameTest.ResolvedNamespace = _namespaces.GetOrCreateId(uri);
                    nameTest.NamespaceUri ??= uri;
                }
            }
            else if (nameTest.NamespaceUri != null && !nameTest.IsNamespaceWildcard && !nameTest.ResolvedNamespace.HasValue)
            {
                // Q{uri}* or Q{uri}name — resolve EQName namespace URI to ID
                nameTest.ResolvedNamespace = string.IsNullOrEmpty(nameTest.NamespaceUri)
                    ? NamespaceId.None
                    : _namespaces.GetOrCreateId(nameTest.NamespaceUri);
            }
            else if (nameTest.Prefix == null && nameTest.LocalName != "*"
                && expr.Axis is not Ast.Axis.Attribute and not Ast.Axis.Namespace)
            {
                // Unprefixed element name — check for default element namespace
                var defaultElementUri = _namespaces.ResolvePrefix("##default-element");
                if (defaultElementUri != null)
                {
                    nameTest.ResolvedNamespace = _namespaces.GetOrCreateId(defaultElementUri);
                    nameTest.NamespaceUri = defaultElementUri;
                }
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
        // Register any xmlns: namespace declarations on this element before resolving children
        if (expr.NamespaceDeclarations != null)
        {
            foreach (var nsDecl in expr.NamespaceDeclarations)
                _namespaces.RegisterNamespace(nsDecl.Prefix, nsDecl.Uri);
        }

        // Scan attributes for xmlns: declarations (from direct element constructors
        // where the parser treats them as regular attributes)
        foreach (var attr in expr.Attributes)
        {
            if (attr is AttributeConstructor attrC)
            {
                if (attrC.Name.Prefix == "xmlns" && attrC.Value is StringLiteral lit)
                {
                    _namespaces.RegisterNamespace(attrC.Name.LocalName, lit.Value);
                }
                else if (string.IsNullOrEmpty(attrC.Name.Prefix) && attrC.Name.LocalName == "xmlns" && attrC.Value is StringLiteral defLit)
                {
                    _namespaces.RegisterNamespace("", defLit.Value);
                }
            }
        }

        // Resolve element name namespace
        var name = expr.Name;
        var resolvedName = name;
        if (!string.IsNullOrEmpty(name.Prefix))
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
                resolvedName = new QName(nsId, name.LocalName, name.Prefix)
                {
                    ExpandedNamespace = uri
                };
            }
        }

        // Recursively rewrite attributes and content (base class doesn't descend into children)
        var rewrittenAttrs = new List<XQueryExpression>(expr.Attributes.Count);
        foreach (var attr in expr.Attributes)
            rewrittenAttrs.Add(Rewrite(attr));

        var rewrittenContent = new List<XQueryExpression>(expr.Content.Count);
        foreach (var content in expr.Content)
            rewrittenContent.Add(Rewrite(content));

        if (resolvedName.Equals(name) && rewrittenAttrs.SequenceEqual(expr.Attributes) && rewrittenContent.SequenceEqual(expr.Content))
            return expr;

        return new ElementConstructor
        {
            Name = resolvedName,
            Attributes = rewrittenAttrs,
            Content = rewrittenContent,
            NamespaceDeclarations = expr.NamespaceDeclarations,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitAttributeConstructor(AttributeConstructor expr)
    {
        // Resolve attribute name namespace (skip unprefixed — no namespace by default)
        var name = expr.Name;
        if (!string.IsNullOrEmpty(name.Prefix) && name.Prefix != "xmlns")
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
                var resolvedExpr = new AttributeConstructor
                {
                    Name = new QName(nsId, name.LocalName, name.Prefix)
                    {
                        ExpandedNamespace = uri
                    },
                    Value = expr.Value,
                    Location = expr.Location
                };
                return base.VisitAttributeConstructor(resolvedExpr);
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
