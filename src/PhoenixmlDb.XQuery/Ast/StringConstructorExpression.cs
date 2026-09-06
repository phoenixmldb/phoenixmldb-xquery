using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// XQuery 3.1/4.0: String constructor expression.
/// Builds a string from a sequence of literal parts and interpolated expressions.
/// Syntax: ``[literal text `{expr}` more text]``
/// </summary>
public sealed class StringConstructorExpression : XQueryExpression
{
    /// <summary>
    /// Parts of the string constructor: either literal strings or expressions.
    /// </summary>
    public required IReadOnlyList<StringConstructorPart> Parts { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitStringConstructor(this);

    public override string ToString()
        => $"``[{string.Join("", Parts)}]``";
}
