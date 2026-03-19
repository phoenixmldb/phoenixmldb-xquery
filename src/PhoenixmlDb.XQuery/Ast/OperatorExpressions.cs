namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Binary operator expression (e.g., a + b, x eq y).
/// </summary>
public sealed class BinaryExpression : XQueryExpression
{
    public required XQueryExpression Left { get; init; }
    public required BinaryOperator Operator { get; init; }
    public required XQueryExpression Right { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitBinaryExpression(this);

    public override string ToString()
    {
        var op = Operator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "div",
            BinaryOperator.IntegerDivide => "idiv",
            BinaryOperator.Modulo => "mod",
            BinaryOperator.Equal => "eq",
            BinaryOperator.NotEqual => "ne",
            BinaryOperator.LessThan => "lt",
            BinaryOperator.LessOrEqual => "le",
            BinaryOperator.GreaterThan => "gt",
            BinaryOperator.GreaterOrEqual => "ge",
            BinaryOperator.GeneralEqual => "=",
            BinaryOperator.GeneralNotEqual => "!=",
            BinaryOperator.GeneralLessThan => "<",
            BinaryOperator.GeneralLessOrEqual => "<=",
            BinaryOperator.GeneralGreaterThan => ">",
            BinaryOperator.GeneralGreaterOrEqual => ">=",
            BinaryOperator.Is => "is",
            BinaryOperator.Precedes => "<<",
            BinaryOperator.Follows => ">>",
            BinaryOperator.And => "and",
            BinaryOperator.Or => "or",
            BinaryOperator.Union => "union",
            BinaryOperator.Intersect => "intersect",
            BinaryOperator.Except => "except",
            BinaryOperator.To => "to",
            BinaryOperator.Concat => "||",
            BinaryOperator.MapLookup => "?",
            BinaryOperator.Otherwise => "otherwise",
            _ => "?"
        };
        return $"({Left} {op} {Right})";
    }
}

/// <summary>
/// Binary operators.
/// </summary>
public enum BinaryOperator
{
    // Arithmetic
    Add,
    Subtract,
    Multiply,
    Divide,
    IntegerDivide,
    Modulo,

    // Value comparison
    Equal,
    NotEqual,
    LessThan,
    LessOrEqual,
    GreaterThan,
    GreaterOrEqual,

    // General comparison
    GeneralEqual,
    GeneralNotEqual,
    GeneralLessThan,
    GeneralLessOrEqual,
    GeneralGreaterThan,
    GeneralGreaterOrEqual,

    // Node comparison
    Is,
    Precedes,
    Follows,

    // Logical
    And,
    Or,

    // Sequence
    Union,
    Intersect,
    Except,

    // Range
    To,

    // String concatenation
    Concat,

    // Map/array lookup (3.1)
    MapLookup,

    // Coalescing (4.0)
    Otherwise
}

/// <summary>
/// Unary operator expression (e.g., -x, +y).
/// </summary>
public sealed class UnaryExpression : XQueryExpression
{
    public required UnaryOperator Operator { get; init; }
    public required XQueryExpression Operand { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitUnaryExpression(this);

    public override string ToString()
    {
        var op = Operator switch
        {
            UnaryOperator.Plus => "+",
            UnaryOperator.Minus => "-",
            UnaryOperator.Not => "not",
            _ => "?"
        };
        return $"({op} {Operand})";
    }
}

/// <summary>
/// Unary operators.
/// </summary>
public enum UnaryOperator
{
    Plus,
    Minus,
    Not
}

/// <summary>
/// Sequence expression (a, b, c).
/// </summary>
public sealed class SequenceExpression : XQueryExpression
{
    public required IReadOnlyList<XQueryExpression> Items { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitSequenceExpression(this);

    public override string ToString()
    {
        return $"({string.Join(", ", Items)})";
    }
}

/// <summary>
/// Range expression (1 to 10).
/// </summary>
public sealed class RangeExpression : XQueryExpression
{
    public required XQueryExpression Start { get; init; }
    public required XQueryExpression End { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitRangeExpression(this);

    public override string ToString() => $"({Start} to {End})";
}

/// <summary>
/// Instance of expression (expr instance of type).
/// </summary>
public sealed class InstanceOfExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitInstanceOfExpression(this);

    public override string ToString() => $"({Expression} instance of {TargetType})";
}

/// <summary>
/// Cast expression (expr cast as type).
/// </summary>
public sealed class CastExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitCastExpression(this);

    public override string ToString() => $"({Expression} cast as {TargetType})";
}

/// <summary>
/// Castable expression (expr castable as type).
/// </summary>
public sealed class CastableExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitCastableExpression(this);

    public override string ToString() => $"({Expression} castable as {TargetType})";
}

/// <summary>
/// Treat expression (expr treat as type).
/// </summary>
public sealed class TreatExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitTreatExpression(this);

    public override string ToString() => $"({Expression} treat as {TargetType})";
}

/// <summary>
/// Arrow expression (expr => func()) - XQuery 3.1.
/// </summary>
public sealed class ArrowExpression : XQueryExpression
{
    public required XQueryExpression Expression { get; init; }
    public required XQueryExpression FunctionCall { get; init; }
    /// <summary>
    /// True for thin arrow (->) which changes the focus/context item.
    /// False for fat arrow (=>) which passes as first argument.
    /// </summary>
    public bool IsThinArrow { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitArrowExpression(this);

    public override string ToString() => $"({Expression} {(IsThinArrow ? "->" : "=>")} {FunctionCall})";
}

/// <summary>
/// Simple map expression (expr ! expr) - XQuery 3.0+.
/// </summary>
public sealed class SimpleMapExpression : XQueryExpression
{
    public required XQueryExpression Left { get; init; }
    public required XQueryExpression Right { get; init; }

    /// <summary>
    /// When true, this expression originated from a path step (/) rather than the
    /// simple map operator (!). Path steps require document-order sorting of results.
    /// </summary>
    public bool IsPathStep { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitSimpleMapExpression(this);

    public override string ToString() => $"({Left} ! {Right})";
}

/// <summary>
/// String concatenation expression (a || b) - XQuery 3.1.
/// </summary>
public sealed class StringConcatExpression : XQueryExpression
{
    public required IReadOnlyList<XQueryExpression> Operands { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitStringConcatExpression(this);

    public override string ToString() => $"({string.Join(" || ", Operands)})";
}
