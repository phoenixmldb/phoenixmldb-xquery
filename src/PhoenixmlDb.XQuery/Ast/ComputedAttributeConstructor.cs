using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Computed attribute constructor (attribute { name } { value }).
/// </summary>
public sealed class ComputedAttributeConstructor : XQueryExpression
{
    public required XQueryExpression NameExpression { get; init; }

    /// <summary>
    /// Fully-resolved QName when the name is a static EQName.
    /// Null when the name is computed from an expression.
    /// </summary>
    public QName? StaticName { get; init; }

    /// <summary>
    /// The name was written as a QName in the query (<c>attribute p:n { }</c>), not computed. An
    /// undeclared prefix in it is then XPST0081, not the XQDY0074 of a computed name.
    /// </summary>
    public bool HasLiteralName { get; init; }

    public required XQueryExpression ValueExpression { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitComputedAttributeConstructor(this);

    public override string ToString()
        => $"attribute {{ {NameExpression} }} {{ {ValueExpression} }}";
}
