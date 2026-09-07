using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Try-catch expression (XQuery 3.0+).
/// </summary>
public sealed class TryCatchExpression : XQueryExpression
{
    public required XQueryExpression TryExpression { get; init; }
    public required IReadOnlyList<CatchClause> CatchClauses { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitTryCatchExpression(this);

    public override string ToString()
    {
        var catches = string.Join(" ", CatchClauses);
        return $"try {{ {TryExpression} }} {catches}";
    }
}
