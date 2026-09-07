namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Sequence expression (a, b, c).
/// </summary>
public sealed class SequenceExpression : XQueryExpression
{
    public required IReadOnlyList<XQueryExpression> Items { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitSequenceExpression(this);

    public override string ToString()
    {
        return $"({string.Join(", ", Items)})";
    }
}
