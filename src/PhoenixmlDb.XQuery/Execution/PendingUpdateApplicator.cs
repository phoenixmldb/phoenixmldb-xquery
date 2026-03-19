using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Interface for node stores that can apply XQuery Update primitives.
/// Implemented by both in-memory (XSLT) and persistent (LMDB) node stores.
/// </summary>
public interface IUpdatableNodeStore
{
    /// <summary>Get a node by its ID.</summary>
    XdmNode? GetNode(NodeId id);

    /// <summary>Add a new node to the store.</summary>
    NodeId AddNode(XdmNode node);

    /// <summary>Remove a node from the store.</summary>
    void RemoveNode(NodeId id);

    /// <summary>Insert a child node at a specific position.</summary>
    void InsertChild(NodeId parent, NodeId child, int position);

    /// <summary>Append a child node as the last child.</summary>
    void AppendChild(NodeId parent, NodeId child);

    /// <summary>Remove a child from its parent.</summary>
    void RemoveChild(NodeId parent, NodeId child);

    /// <summary>Get the children of a node.</summary>
    IReadOnlyList<NodeId> GetChildren(NodeId parent);

    /// <summary>Replace the text value of a node.</summary>
    void SetValue(NodeId node, string value);

    /// <summary>Rename a node (change local name and/or namespace).</summary>
    void RenameNode(NodeId node, string localName, NamespaceId ns);
}

/// <summary>
/// Applies a Pending Update List (PUL) to a node store.
/// Per XQuery Update Facility spec §3.4.1, updates are applied
/// atomically in a specific order to prevent conflicts.
/// </summary>
public static class PendingUpdateApplicator
{
    /// <summary>
    /// Applies a PUL to a node store. Returns the number of primitives applied.
    /// </summary>
    public static int Apply(PendingUpdateList pul, IUpdatableNodeStore store)
    {
        if (!pul.HasUpdates) return 0;
        var applied = 0;

        // Phase 1-2: Inserts (into first, then before/after)
        foreach (var prim in pul.Primitives.OfType<InsertPrimitive>())
        {
            if (ApplyInsert(prim, store)) applied++;
        }

        // Phase 3: Replace node
        foreach (var prim in pul.Primitives.OfType<ReplaceNodePrimitive>())
        {
            if (ApplyReplaceNode(prim, store)) applied++;
        }

        // Phase 4: Replace value
        foreach (var prim in pul.Primitives.OfType<ReplaceValuePrimitive>())
        {
            if (ApplyReplaceValue(prim, store)) applied++;
        }

        // Phase 5: Rename
        foreach (var prim in pul.Primitives.OfType<RenamePrimitive>())
        {
            if (ApplyRename(prim, store)) applied++;
        }

        // Phase 6: Delete (last to avoid dangling references)
        foreach (var prim in pul.Primitives.OfType<DeletePrimitive>())
        {
            if (ApplyDelete(prim, store)) applied++;
        }

        return applied;
    }

    private static bool ApplyInsert(InsertPrimitive prim, IUpdatableNodeStore store)
    {
        if (prim.Target is not XdmNode target) return false;
        var sourceNodes = GetSourceNodes(prim.Source);
        if (sourceNodes.Count == 0) return false;

        switch (prim.Position)
        {
            case InsertPosition.Into:
            case InsertPosition.AsLastInto:
                foreach (var node in sourceNodes)
                {
                    node.Parent = target.Id;  // NodeId? accepts NodeId
                    var newId = store.AddNode(node);
                    store.AppendChild(target.Id, newId);
                }
                return true;

            case InsertPosition.AsFirstInto:
                for (var i = sourceNodes.Count - 1; i >= 0; i--)
                {
                    sourceNodes[i].Parent = target.Id;
                    var newId = store.AddNode(sourceNodes[i]);
                    store.InsertChild(target.Id, newId, 0);
                }
                return true;

            case InsertPosition.Before:
            case InsertPosition.After:
                if (!target.Parent.HasValue) return false;
                var parentId = target.Parent.Value;
                var children = store.GetChildren(parentId);
                var idx = -1;
                for (var i = 0; i < children.Count; i++)
                {
                    if (children[i] == target.Id) { idx = i; break; }
                }
                if (idx < 0) return false;
                var insertIdx = prim.Position == InsertPosition.Before ? idx : idx + 1;
                foreach (var node in sourceNodes)
                {
                    node.Parent = parentId;
                    var newId = store.AddNode(node);
                    store.InsertChild(parentId, newId, insertIdx++);
                }
                return true;
        }

        return false;
    }

    private static bool ApplyDelete(DeletePrimitive prim, IUpdatableNodeStore store)
    {
        if (prim.Target is not XdmNode target) return false;
        if (target.Parent.HasValue && target.Parent.Value != NodeId.None)
            store.RemoveChild(target.Parent.Value, target.Id);
        store.RemoveNode(target.Id);
        return true;
    }

    private static bool ApplyReplaceNode(ReplaceNodePrimitive prim, IUpdatableNodeStore store)
    {
        if (prim.Target is not XdmNode target) return false;
        if (!target.Parent.HasValue) return false;

        var replacements = GetSourceNodes(prim.Replacement);
        if (replacements.Count == 0) return false;

        var parentId = target.Parent.Value;
        var children = store.GetChildren(parentId);
        var idx = -1;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] == target.Id) { idx = i; break; }
        }
        if (idx < 0) return false;

        // Remove old node
        store.RemoveChild(parentId, target.Id);
        store.RemoveNode(target.Id);

        // Insert replacements at same position
        foreach (var node in replacements)
        {
            node.Parent = parentId;
            var newId = store.AddNode(node);
            store.InsertChild(parentId, newId, idx++);
        }

        return true;
    }

    private static bool ApplyReplaceValue(ReplaceValuePrimitive prim, IUpdatableNodeStore store)
    {
        if (prim.Target is not XdmNode target) return false;
        store.SetValue(target.Id, prim.Value?.ToString() ?? "");
        return true;
    }

    private static bool ApplyRename(RenamePrimitive prim, IUpdatableNodeStore store)
    {
        if (prim.Target is not XdmNode target) return false;
        store.RenameNode(target.Id, prim.NewName.LocalName, prim.NewName.Namespace);
        return true;
    }

    private static List<XdmNode> GetSourceNodes(object source)
    {
        var nodes = new List<XdmNode>();
        if (source is XdmNode node)
            nodes.Add(node);
        else if (source is object?[] arr)
            foreach (var item in arr)
                if (item is XdmNode n)
                    nodes.Add(n);
        return nodes;
    }
}
