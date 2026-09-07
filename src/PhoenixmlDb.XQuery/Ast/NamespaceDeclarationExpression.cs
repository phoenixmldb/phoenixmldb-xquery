using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// declare namespace prefix = "uri";
/// </summary>
public sealed class NamespaceDeclarationExpression : XQueryExpression
{
    public required string Prefix { get; init; }
    public required string Uri { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitNamespaceDeclaration(this);
}
