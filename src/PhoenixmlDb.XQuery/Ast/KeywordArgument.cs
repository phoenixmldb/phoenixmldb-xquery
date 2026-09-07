using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// XPath 4.0: keyword argument in a function call (name := value).
/// </summary>
public sealed class KeywordArgument : XQueryExpression
{
    public required string Name { get; init; }
    public required XQueryExpression Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitKeywordArgument(this);
}
