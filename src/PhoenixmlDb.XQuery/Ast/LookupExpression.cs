using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Lookup expression for maps and arrays (expr?key or expr?*).
/// </summary>
public sealed class LookupExpression : XQueryExpression
{
    public required XQueryExpression Base { get; init; }
    public XQueryExpression? Key { get; init; } // null for wildcard lookup (?*)

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitLookupExpression(this);

    public override string ToString()
        => Key != null ? $"{Base}?{Key}" : $"{Base}?*";
}
