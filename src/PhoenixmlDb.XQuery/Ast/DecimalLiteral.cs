using System.Numerics;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Decimal literal (e.g., 3.14, -0.5).
/// </summary>
public sealed class DecimalLiteral : LiteralExpression
{
    public required decimal Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitDecimalLiteral(this);

    public override string ToString() => Value.ToString();
}
