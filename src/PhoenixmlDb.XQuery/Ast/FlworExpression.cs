using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// FLWOR expression (for/let/where/order by/return).
/// </summary>
public sealed class FlworExpression : XQueryExpression
{
    /// <summary>
    /// The clauses (for, let, where, order by, group by, count, window).
    /// </summary>
    public required IReadOnlyList<FlworClause> Clauses { get; init; }

    /// <summary>
    /// The return expression.
    /// </summary>
    public required XQueryExpression ReturnExpression { get; init; }

    /// <summary>
    /// XPath 4.0: optional otherwise expression — evaluated when FLWOR produces empty sequence.
    /// </summary>
    public XQueryExpression? OtherwiseExpression { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitFlworExpression(this);

    public override string ToString()
    {
        var clauses = string.Join(" ", Clauses);
        return $"{clauses} return {ReturnExpression}";
    }
}
