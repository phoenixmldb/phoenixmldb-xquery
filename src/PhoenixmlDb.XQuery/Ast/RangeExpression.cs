namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Range expression (1 to 10).
/// </summary>
public sealed class RangeExpression : XQueryExpression
{
    public required XQueryExpression Start { get; init; }
    public required XQueryExpression End { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitRangeExpression(this);

    public override string ToString() => $"({Start} to {End})";
}
