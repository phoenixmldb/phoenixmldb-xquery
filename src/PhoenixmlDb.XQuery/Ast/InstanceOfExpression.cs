namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Instance of expression (expr instance of type).
/// </summary>
public sealed class InstanceOfExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitInstanceOfExpression(this);

    public override string ToString() => $"({Expression} instance of {TargetType})";
}
