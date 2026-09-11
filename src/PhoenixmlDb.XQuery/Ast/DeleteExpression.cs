using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// delete node(s) $target
/// </summary>
public sealed class DeleteExpression : UpdateExpression
{
    /// <summary>The node(s) to delete.</summary>
    public required XQueryExpression Target { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitDeleteExpression(this);
}
