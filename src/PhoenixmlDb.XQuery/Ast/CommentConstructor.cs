using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Comment constructor (<!-- comment --> or comment { value }).
/// </summary>
public sealed class CommentConstructor : XQueryExpression
{
    public required XQueryExpression Value { get; init; }
    public bool IsDirect { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitCommentConstructor(this);

    public override string ToString()
        => IsDirect ? $"<!--{Value}-->" : $"comment {{ {Value} }}";
}
