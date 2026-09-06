namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Cast expression (expr cast as type).
/// </summary>
public sealed class CastExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitCastExpression(this);

    public override string ToString() => $"({Expression} cast as {TargetType})";
}
