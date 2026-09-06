using System.Numerics;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Double literal (e.g., 1.5e10, 2.5E-3).
/// </summary>
public sealed class DoubleLiteral : LiteralExpression
{
    public required double Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitDoubleLiteral(this);

    public override string ToString() => Value.ToString("G");
}
