using System.Numerics;

namespace PhoenixmlDb.XQuery.Ast;

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
