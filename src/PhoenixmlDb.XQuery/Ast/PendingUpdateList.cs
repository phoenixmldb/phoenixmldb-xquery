using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

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
