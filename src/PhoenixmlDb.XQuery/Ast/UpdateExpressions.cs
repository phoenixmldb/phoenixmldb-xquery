using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

// ═══════════════════════════════════════════════════════════════════════════
// XQuery Update Facility 3.0 — AST node definitions
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Base class for all XQuery Update expressions.
/// Update expressions return Pending Update Lists (PULs) instead of values.
/// </summary>
public abstract class UpdateExpression : XQueryExpression
{
    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => default!;
}

/// <summary>
/// insert node(s) (before|after|as first into|as last into|into) target
/// </summary>
public sealed class InsertExpression : UpdateExpression
{
    /// <summary>The node(s) to insert.</summary>
    public required XQueryExpression Source { get; init; }
    /// <summary>The target node (parent or sibling).</summary>
    public required XQueryExpression Target { get; init; }
    /// <summary>Where to insert relative to the target.</summary>
    public required InsertPosition Position { get; init; }
}

/// <summary>
/// Position for insert operations.
/// </summary>
public enum InsertPosition
{
    /// <summary>insert node ... into $target (as child, implementation-defined position)</summary>
    Into,
    /// <summary>insert node ... as first into $target</summary>
    AsFirstInto,
    /// <summary>insert node ... as last into $target</summary>
    AsLastInto,
    /// <summary>insert node ... before $target (as preceding sibling)</summary>
    Before,
    /// <summary>insert node ... after $target (as following sibling)</summary>
    After
}

/// <summary>
/// delete node(s) $target
/// </summary>
public sealed class DeleteExpression : UpdateExpression
{
    /// <summary>The node(s) to delete.</summary>
    public required XQueryExpression Target { get; init; }
}

/// <summary>
/// replace node $target with $replacement
/// </summary>
public sealed class ReplaceNodeExpression : UpdateExpression
{
    /// <summary>The node to replace.</summary>
    public required XQueryExpression Target { get; init; }
    /// <summary>The replacement node(s).</summary>
    public required XQueryExpression Replacement { get; init; }
}

/// <summary>
/// replace value of node $target with $value
/// </summary>
public sealed class ReplaceValueExpression : UpdateExpression
{
    /// <summary>The node whose value to replace.</summary>
    public required XQueryExpression Target { get; init; }
    /// <summary>The new value.</summary>
    public required XQueryExpression Value { get; init; }
}

/// <summary>
/// rename node $target as $newName
/// </summary>
public sealed class RenameExpression : UpdateExpression
{
    /// <summary>The node to rename.</summary>
    public required XQueryExpression Target { get; init; }
    /// <summary>The new name (QName expression).</summary>
    public required XQueryExpression NewName { get; init; }
}

/// <summary>
/// copy $var := $source modify (update-expr) return $result
/// Functional update — creates a modified copy without side effects.
/// </summary>
public sealed class TransformExpression : UpdateExpression
{
    /// <summary>Copy bindings: variable := source pairs.</summary>
    public required IReadOnlyList<TransformCopyBinding> CopyBindings { get; init; }
    /// <summary>The modify clause — update expressions applied to the copies.</summary>
    public required XQueryExpression ModifyExpr { get; init; }
    /// <summary>The return clause — the result expression.</summary>
    public required XQueryExpression ReturnExpr { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitTransformExpression(this);
}

/// <summary>
/// A single copy binding in a transform expression: $var := expr
/// </summary>
public sealed class TransformCopyBinding
{
    public required QName Variable { get; init; }
    public required XQueryExpression Expression { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
// Pending Update List — runtime structures for collecting and applying updates
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A single update primitive in a Pending Update List.
/// </summary>
public abstract class UpdatePrimitive
{
    /// <summary>The target node being modified.</summary>
    public required object Target { get; init; }
}

/// <summary>Insert a node relative to a target.</summary>
public sealed class InsertPrimitive : UpdatePrimitive
{
    public required object Source { get; init; }
    public required InsertPosition Position { get; init; }
}

/// <summary>Delete a node.</summary>
public sealed class DeletePrimitive : UpdatePrimitive { }

/// <summary>Replace a node with another.</summary>
public sealed class ReplaceNodePrimitive : UpdatePrimitive
{
    public required object Replacement { get; init; }
}

/// <summary>Replace a node's value (text content).</summary>
public sealed class ReplaceValuePrimitive : UpdatePrimitive
{
    public required object Value { get; init; }
}

/// <summary>Rename a node.</summary>
public sealed class RenamePrimitive : UpdatePrimitive
{
    public required QName NewName { get; init; }
}

/// <summary>
/// Pending Update List — collects update primitives during query evaluation.
/// Applied atomically after the query completes.
/// </summary>
public sealed class PendingUpdateList
{
    private readonly List<UpdatePrimitive> _primitives = [];

    /// <summary>The collected update primitives.</summary>
    public IReadOnlyList<UpdatePrimitive> Primitives => _primitives;

    /// <summary>Whether any updates have been collected.</summary>
    public bool HasUpdates => _primitives.Count > 0;

    public void AddInsert(object target, object source, InsertPosition position)
        => _primitives.Add(new InsertPrimitive { Target = target, Source = source, Position = position });

    public void AddDelete(object target)
        => _primitives.Add(new DeletePrimitive { Target = target });

    public void AddReplaceNode(object target, object replacement)
        => _primitives.Add(new ReplaceNodePrimitive { Target = target, Replacement = replacement });

    public void AddReplaceValue(object target, object value)
        => _primitives.Add(new ReplaceValuePrimitive { Target = target, Value = value });

    public void AddRename(object target, QName newName)
        => _primitives.Add(new RenamePrimitive { Target = target, NewName = newName });

    /// <summary>Merge another PUL into this one.</summary>
    public void Merge(PendingUpdateList other)
        => _primitives.AddRange(other._primitives);

    /// <summary>Clear all collected updates.</summary>
    public void Clear() => _primitives.Clear();
}
