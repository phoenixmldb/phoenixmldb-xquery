using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Text constructor (text { value }).
/// </summary>
public sealed class TextConstructor : XQueryExpression
{
    public required XQueryExpression Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitTextConstructor(this);

    public override string ToString() => $"text {{ {Value} }}";
}
