using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// replace node $target with $replacement
/// </summary>
public sealed class ReplaceNodeExpression : UpdateExpression
{
    /// <summary>The node to replace.</summary>
    public required XQueryExpression Target { get; init; }
    /// <summary>The replacement node(s).</summary>
    public required XQueryExpression Replacement { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitReplaceNodeExpression(this);
}
