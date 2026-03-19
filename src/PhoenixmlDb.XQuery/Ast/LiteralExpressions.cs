using System.Numerics;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Base for literal value expressions.
/// </summary>
public abstract class LiteralExpression : XQueryExpression;

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

/// <summary>
/// Decimal literal (e.g., 3.14, -0.5).
/// </summary>
public sealed class DecimalLiteral : LiteralExpression
{
    public required decimal Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitDecimalLiteral(this);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Double literal (e.g., 1.5e10, 2.5E-3).
/// </summary>
public sealed class DoubleLiteral : LiteralExpression
{
    public required double Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitDoubleLiteral(this);

    public override string ToString() => Value.ToString("G");
}

/// <summary>
/// String literal (e.g., "hello", 'world').
/// </summary>
public sealed class StringLiteral : LiteralExpression
{
    public required string Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitStringLiteral(this);

    public override string ToString() => $"\"{Value}\"";
}

/// <summary>
/// Boolean literal (true() or false()).
/// </summary>
public sealed class BooleanLiteral : LiteralExpression
{
    public required bool Value { get; init; }

    public static BooleanLiteral True { get; } = new() { Value = true };
    public static BooleanLiteral False { get; } = new() { Value = false };

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitBooleanLiteral(this);

    public override string ToString() => Value ? "true()" : "false()";
}

/// <summary>
/// Empty sequence literal ().
/// </summary>
public sealed class EmptySequence : XQueryExpression
{
    public static EmptySequence Instance { get; } = new();

    private EmptySequence() { }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitEmptySequence(this);

    public override string ToString() => "()";
}

/// <summary>
/// Context item expression (.).
/// </summary>
public sealed class ContextItemExpression : XQueryExpression
{
    public static ContextItemExpression Instance { get; } = new();

    private ContextItemExpression() { }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitContextItem(this);

    public override string ToString() => ".";
}
