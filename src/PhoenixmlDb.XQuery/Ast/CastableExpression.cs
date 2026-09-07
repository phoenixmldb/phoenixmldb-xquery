namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Castable expression (expr castable as type).
/// </summary>
public sealed class CastableExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitCastableExpression(this);

    public override string ToString() => $"({Expression} castable as {TargetType})";
}
