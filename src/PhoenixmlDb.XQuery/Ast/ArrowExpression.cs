namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Arrow expression (expr => func()) - XQuery 3.1.
/// </summary>
public sealed class ArrowExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XQueryExpression FunctionCall { get; init; }
    /// <summary>
    /// True for thin arrow (->) which changes the focus/context item.
    /// False for fat arrow (=>) which passes as first argument.
    /// </summary>
    public bool IsThinArrow { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitArrowExpression(this);

    public override string ToString() => $"({Expression} {(IsThinArrow ? "->" : "=>")} {FunctionCall})";
}
