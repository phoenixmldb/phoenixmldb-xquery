namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Unary operator expression (e.g., -x, +y).
/// </summary>
public sealed class UnaryExpression : XQueryExpression
{
    public required UnaryOperator Operator { get; init; }
    public required XQueryExpression Operand { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitUnaryExpression(this);

    public override string ToString()
    {
        var op = Operator switch
        {
            UnaryOperator.Plus => "+",
            UnaryOperator.Minus => "-",
            UnaryOperator.Not => "not",
            _ => "?"
        };
        return $"({op} {Operand})";
    }
}
