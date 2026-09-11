using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// replace value of node $target with $value
/// </summary>
public sealed class ReplaceValueExpression : UpdateExpression
{
    /// <summary>The node whose value to replace.</summary>
    public required XQueryExpression Target { get; init; }
    /// <summary>The new value.</summary>
    public required XQueryExpression Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitReplaceValueExpression(this);
}
