using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// FLWOR expression (for/let/where/order by/return).
/// </summary>
public sealed class FlworExpression : XQueryExpression
{
    /// <summary>
    /// The clauses (for, let, where, order by, group by, count, window).
    /// </summary>
    public required IReadOnlyList<FlworClause> Clauses { get; init; }

    /// <summary>
    /// The return expression.
    /// </summary>
    public required XQueryExpression ReturnExpression { get; init; }

    /// <summary>
    /// XPath 4.0: optional otherwise expression — evaluated when FLWOR produces empty sequence.
    /// </summary>
    public XQueryExpression? OtherwiseExpression { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitFlworExpression(this);

    public override string ToString()
    {
        var clauses = string.Join(" ", Clauses);
        return $"{clauses} return {ReturnExpression}";
    }
}

/// <summary>
/// Base for FLWOR clauses.
/// </summary>
public abstract class FlworClause;

/// <summary>
/// For clause (for $var in expr).
/// </summary>
public sealed class ForClause : FlworClause
{
    public required IReadOnlyList<ForBinding> Bindings { get; init; }
    /// <summary>
    /// True for "for member" (XPath 4.0) — iterates over array members instead of sequence items.
    /// </summary>
    public bool IsMember { get; init; }

    public override string ToString()
    {
        return (IsMember ? "for member " : "for ") + string.Join(", ", Bindings);
    }
}

/// <summary>
/// A single for binding.
/// </summary>
public sealed class ForBinding
{
    /// <summary>
    /// Variable name.
    /// </summary>
    public required QName Variable { get; init; }

    /// <summary>
    /// Optional type declaration.
    /// </summary>
    public XdmSequenceType? TypeDeclaration { get; init; }

    /// <summary>
    /// Whether to allow empty sequences (for $x allowing empty in ...).
    /// </summary>
    public bool AllowingEmpty { get; init; }

    /// <summary>
    /// Optional positional variable (for $x at $i in ...).
    /// </summary>
    public QName? PositionalVariable { get; init; }

    /// <summary>
    /// The expression to iterate over.
    /// </summary>
    public required XQueryExpression Expression { get; init; }

    public override string ToString()
    {
        var type = TypeDeclaration != null ? $" as {TypeDeclaration}" : "";
        var allowing = AllowingEmpty ? " allowing empty" : "";
        var pos = PositionalVariable != null ? $" at ${PositionalVariable.Value.LocalName}" : "";
        return $"${Variable.LocalName}{type}{allowing}{pos} in {Expression}";
    }
}

/// <summary>
/// Let clause (let $var := expr).
/// </summary>
public sealed class LetClause : FlworClause
{
    public required IReadOnlyList<LetBinding> Bindings { get; init; }

    public override string ToString()
    {
        return "let " + string.Join(", ", Bindings);
    }
}

/// <summary>
/// A single let binding.
/// </summary>
public sealed class LetBinding
{
    /// <summary>
    /// Variable name.
    /// </summary>
    public required QName Variable { get; init; }

    /// <summary>
    /// Optional type declaration.
    /// </summary>
    public XdmSequenceType? TypeDeclaration { get; init; }

    /// <summary>
    /// The expression to bind.
    /// </summary>
    public required XQueryExpression Expression { get; init; }

    public override string ToString()
    {
        var type = TypeDeclaration != null ? $" as {TypeDeclaration}" : "";
        return $"${Variable.LocalName}{type} := {Expression}";
    }
}

/// <summary>
/// Where clause (where condition).
/// </summary>
public sealed class WhereClause : FlworClause
{
    public required XQueryExpression Condition { get; init; }

    public override string ToString() => $"where {Condition}";
}

/// <summary>
/// Order by clause.
/// </summary>
public sealed class OrderByClause : FlworClause
{
    /// <summary>
    /// Whether this is a stable sort.
    /// </summary>
    public bool Stable { get; init; }

    public required IReadOnlyList<OrderSpec> OrderSpecs { get; init; }

    public override string ToString()
    {
        var stable = Stable ? "stable " : "";
        return $"{stable}order by {string.Join(", ", OrderSpecs)}";
    }
}

/// <summary>
/// A single order specification.
/// </summary>
public sealed class OrderSpec
{
    public required XQueryExpression Expression { get; init; }
    public OrderDirection Direction { get; init; } = OrderDirection.Ascending;
    public EmptyOrder EmptyOrder { get; init; } = EmptyOrder.Least;
    public string? Collation { get; init; }

    public override string ToString()
    {
        var dir = Direction == OrderDirection.Descending ? " descending" : "";
        var empty = EmptyOrder == EmptyOrder.Greatest ? " empty greatest" : "";
        var coll = Collation != null ? $" collation \"{Collation}\"" : "";
        return $"{Expression}{dir}{empty}{coll}";
    }
}

/// <summary>
/// Sort direction.
/// </summary>
public enum OrderDirection
{
    Ascending,
    Descending
}

/// <summary>
/// Where empty values sort.
/// </summary>
public enum EmptyOrder
{
    Least,
    Greatest
}

/// <summary>
/// Group by clause (XQuery 3.0+).
/// </summary>
public sealed class GroupByClause : FlworClause
{
    public required IReadOnlyList<GroupingSpec> GroupingSpecs { get; init; }

    public override string ToString()
    {
        return $"group by {string.Join(", ", GroupingSpecs)}";
    }
}

/// <summary>
/// A single grouping specification.
/// </summary>
public sealed class GroupingSpec
{
    public required QName Variable { get; init; }
    public XQueryExpression? Expression { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
    public string? Collation { get; init; }

    public override string ToString()
    {
        var expr = Expression != null ? $" := {Expression}" : "";
        var coll = Collation != null ? $" collation \"{Collation}\"" : "";
        return $"${Variable.LocalName}{expr}{coll}";
    }
}

/// <summary>
/// Count clause (XQuery 3.0+).
/// </summary>
public sealed class CountClause : FlworClause
{
    public required QName Variable { get; init; }

    public override string ToString() => $"count ${Variable.LocalName}";
}

/// <summary>
/// While clause (XQuery 4.0) — terminates FLWOR iteration when condition becomes false.
/// </summary>
public sealed class WhileClause : FlworClause
{
    public required XQueryExpression Condition { get; init; }

    public override string ToString() => $"while ({Condition})";
}

/// <summary>
/// Window clause for sliding/tumbling windows (XQuery 3.0+).
/// </summary>
public sealed class WindowClause : FlworClause
{
    public required WindowKind Kind { get; init; }
    public required QName Variable { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
    public required XQueryExpression Expression { get; init; }
    public required WindowCondition Start { get; init; }
    public WindowCondition? End { get; init; }
    /// <summary>True if the end condition uses "only end" — unclosed windows are not emitted.</summary>
    public bool OnlyEnd { get; init; }

    public override string ToString()
    {
        var kind = Kind == WindowKind.Tumbling ? "tumbling" : "sliding";
        return $"for {kind} window ${Variable.LocalName} in {Expression} start {Start}" +
               (End != null ? $" end {End}" : "");
    }
}

/// <summary>
/// Kind of window.
/// </summary>
public enum WindowKind
{
    Tumbling,
    Sliding
}

/// <summary>
/// Window start/end condition.
/// </summary>
public sealed class WindowCondition
{
    public QName? CurrentItem { get; init; }
    public QName? PreviousItem { get; init; }
    public QName? NextItem { get; init; }
    public QName? Position { get; init; }
    public required XQueryExpression When { get; init; }
}
