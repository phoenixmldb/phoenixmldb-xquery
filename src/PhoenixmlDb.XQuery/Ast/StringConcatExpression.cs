namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// String concatenation expression (a || b) - XQuery 3.1.
/// </summary>
public sealed class StringConcatExpression : XQueryExpression
{
    public required IReadOnlyList<XQueryExpression> Operands { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitStringConcatExpression(this);

    public override string ToString() => $"({string.Join(" || ", Operands)})";
}
