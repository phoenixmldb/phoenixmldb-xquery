namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Binary operator expression (e.g., a + b, x eq y).
/// </summary>
public sealed class BinaryExpression : XQueryExpression
{
    public required XQueryExpression Left { get; init; }
    public required BinaryOperator Operator { get; init; }
    public required XQueryExpression Right { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitBinaryExpression(this);

    public override string ToString()
    {
        var op = Operator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "div",
            BinaryOperator.IntegerDivide => "idiv",
            BinaryOperator.Modulo => "mod",
            BinaryOperator.Equal => "eq",
            BinaryOperator.NotEqual => "ne",
            BinaryOperator.LessThan => "lt",
            BinaryOperator.LessOrEqual => "le",
            BinaryOperator.GreaterThan => "gt",
            BinaryOperator.GreaterOrEqual => "ge",
            BinaryOperator.GeneralEqual => "=",
            BinaryOperator.GeneralNotEqual => "!=",
            BinaryOperator.GeneralLessThan => "<",
            BinaryOperator.GeneralLessOrEqual => "<=",
            BinaryOperator.GeneralGreaterThan => ">",
            BinaryOperator.GeneralGreaterOrEqual => ">=",
            BinaryOperator.Is => "is",
            BinaryOperator.Precedes => "<<",
            BinaryOperator.Follows => ">>",
            BinaryOperator.And => "and",
            BinaryOperator.Or => "or",
            BinaryOperator.Union => "union",
            BinaryOperator.Intersect => "intersect",
            BinaryOperator.Except => "except",
            BinaryOperator.To => "to",
            BinaryOperator.Concat => "||",
            BinaryOperator.MapLookup => "?",
            BinaryOperator.Otherwise => "otherwise",
            _ => "?"
        };
        return $"({Left} {op} {Right})";
    }
}
