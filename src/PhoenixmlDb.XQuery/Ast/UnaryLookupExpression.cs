using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Unary lookup (context item lookup): ?key or ?*.
/// </summary>
public sealed class UnaryLookupExpression : XQueryExpression
{
    public XQueryExpression? Key { get; init; } // null for wildcard lookup (?*)

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitUnaryLookupExpression(this);

    public override string ToString()
        => Key != null ? $"?{Key}" : "?*";
}
