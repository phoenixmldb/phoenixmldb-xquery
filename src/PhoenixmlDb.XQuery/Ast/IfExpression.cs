using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// If-then-else expression.
/// </summary>
public sealed class IfExpression : XQueryExpression
{
    public required XQueryExpression Condition { get; init; }
    public required XQueryExpression Then { get; init; }
    /// <summary>
    /// Else branch. Null for XQuery 4.0 braced-if (defaults to empty sequence).
    /// </summary>
    public XQueryExpression? Else { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitIfExpression(this);

    public override string ToString()
        => Else != null
            ? $"if ({Condition}) then {Then} else {Else}"
            : $"if ({Condition}) {{ {Then} }}";
}
