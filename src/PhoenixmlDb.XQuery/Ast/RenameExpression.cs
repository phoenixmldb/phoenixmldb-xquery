using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// rename node $target as $newName
/// </summary>
public sealed class RenameExpression : UpdateExpression
{
    /// <summary>The node to rename.</summary>
    public required XQueryExpression Target { get; init; }
    /// <summary>The new name (QName expression).</summary>
    public required XQueryExpression NewName { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitRenameExpression(this);
}
