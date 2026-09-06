using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Quantified expression (some/every $x in ... satisfies ...).
/// </summary>
public sealed class QuantifiedExpression : XQueryExpression
{
    public required Quantifier Quantifier { get; init; }
    public required IReadOnlyList<QuantifiedBinding> Bindings { get; init; }
    public required XQueryExpression Satisfies { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitQuantifiedExpression(this);

    public override string ToString()
    {
        var q = Quantifier == Quantifier.Some ? "some" : "every";
        var bindings = string.Join(", ", Bindings);
        return $"{q} {bindings} satisfies {Satisfies}";
    }
}
