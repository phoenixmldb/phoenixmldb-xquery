using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Array constructor (XQuery 3.1): [ item, item, ... ] or array { expr }.
/// </summary>
public sealed class ArrayConstructor : XQueryExpression
{
    public ArrayConstructorKind Kind { get; init; }
    public required IReadOnlyList<XQueryExpression> Members { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitArrayConstructor(this);

    public override string ToString()
    {
        if (Kind == ArrayConstructorKind.Square)
            return $"[ {string.Join(", ", Members)} ]";
        return $"array {{ {string.Join(", ", Members)} }}";
    }
}
