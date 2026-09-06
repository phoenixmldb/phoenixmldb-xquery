namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Treat expression (expr treat as type).
/// </summary>
public sealed class TreatExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitTreatExpression(this);

    public override string ToString() => $"({Expression} treat as {TargetType})";
}
