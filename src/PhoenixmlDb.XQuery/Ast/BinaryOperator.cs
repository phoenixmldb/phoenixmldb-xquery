namespace PhoenixmlDb.XQuery.Ast;

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
