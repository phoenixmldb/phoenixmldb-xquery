using System.Numerics;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Context item expression (.).
/// </summary>
public sealed class ContextItemExpression : XQueryExpression
{
    public static ContextItemExpression Instance { get; } = new();

    private ContextItemExpression() { }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitContextItem(this);

    public override string ToString() => ".";
}
