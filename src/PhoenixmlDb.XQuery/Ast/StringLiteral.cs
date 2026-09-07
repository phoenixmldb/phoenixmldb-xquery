using System.Numerics;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// String literal (e.g., "hello", 'world').
/// </summary>
public sealed class StringLiteral : LiteralExpression
{
    public required string Value { get; init; }

    /// <summary>
    /// When true, this string literal contains content from character references (e.g. &amp;#x20;)
    /// or CDATA sections and must NOT be stripped as boundary whitespace.
    /// </summary>
    public bool ContainsCharacterReferences { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitStringLiteral(this);

    public override string ToString() => $"\"{Value}\"";
}
