using System.Numerics;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Integer literal (e.g., 42, -1, 0).
/// Value is long for values in range, BigInteger for larger values.
/// </summary>
public sealed class IntegerLiteral : LiteralExpression
{
    public required object Value { get; init; }

    /// <summary>Gets the value as long, or null if it's a BigInteger.</summary>
    public long? LongValue => Value is long l ? l : null;

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitIntegerLiteral(this);

    public override string ToString() => Value.ToString()!;
}
