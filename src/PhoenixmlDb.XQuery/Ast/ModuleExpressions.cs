using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Wraps a module with prolog declarations and a query body.
/// </summary>
public sealed class ModuleExpression : XQueryExpression
{
    /// <summary>
    /// Prolog declarations (variable bindings, function declarations, namespace declarations).
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Declarations { get; init; }

    /// <summary>
    /// The query body expression.
    /// </summary>
    public required XQueryExpression Body { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitModuleExpression(this);
}

/// <summary>
/// declare variable $name := expr;
/// </summary>
public sealed class VariableDeclarationExpression : XQueryExpression
{
    public required QName Name { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
    public required XQueryExpression Value { get; init; }
    public bool IsExternal { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitVariableDeclaration(this);
}

/// <summary>
/// declare function name($params) [as type] { body };
/// </summary>
public sealed class FunctionDeclarationExpression : XQueryExpression
{
    public required QName Name { get; init; }
    public required IReadOnlyList<FunctionParameter> Parameters { get; init; }
    public XdmSequenceType? ReturnType { get; init; }
    public required XQueryExpression Body { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitFunctionDeclaration(this);
}

/// <summary>
/// declare namespace prefix = "uri";
/// </summary>
public sealed class NamespaceDeclarationExpression : XQueryExpression
{
    public required string Prefix { get; init; }
    public required string Uri { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitNamespaceDeclaration(this);
}
