using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Filter expression (primary expression with predicates).
/// </summary>
public sealed class FilterExpression : XQueryExpression
{
    /// <summary>
    /// The primary expression to filter.
    /// </summary>
    public required XQueryExpression Primary { get; init; }

    /// <summary>
    /// The predicates.
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Predicates { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitFilterExpression(this);

    public override string ToString()
    {
        var preds = string.Concat(Predicates.Select(p => $"[{p}]"));
        return $"{Primary}{preds}";
    }
}
