using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// The opt-in for a node store that XQuery Update may change. A query's pending updates are
/// applied, when it ends, to the store of each target node (<see cref="XdmNode.Store"/>); a store
/// that implements this interface is changed through it, and a target in a store that does not
/// gives <c>phx:XUDY9001</c> (namespace <c>https://schemas.phoenixml.dev/2026/functions</c>) with
/// nothing changed, in any store.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XdmDocumentStore"/> (documents it loads and nodes a query constructs) implements it,
/// explicitly, as does the store of a <c>copy</c> clause. A store of documents that must not
/// change in place, such as a database's stored documents, does not implement it: an update of
/// such a node is an error, and <c>copy $c := $node modify ... return $c</c> changes an in-memory
/// copy.
/// </para>
/// <para>
/// Every target is checked before any change. The engine calls the members only while it applies
/// a list, on one thread.
/// </para>
/// </remarks>
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

/// <summary>A store that finishes its work once a whole update list is applied (string values, for one).</summary>
internal interface IUpdateBatch
{
    void UpdatesApplied();
}

/// <summary>
/// What a store needs beyond <see cref="IUpdatableNodeStore"/> for attributes and prefixed names:
/// upd:insertAttributes, upd:replaceNode of an attribute, upd:rename with a prefix.
/// </summary>
internal interface IAttributeUpdates
{
    void InsertAttributes(NodeId element, IReadOnlyList<XdmAttribute> attributes);

    bool ReplaceAttribute(NodeId element, NodeId target, IReadOnlyList<XdmAttribute> replacements);

    void RenameNode(NodeId node, string localName, NamespaceId ns, string? newPrefix);
}

/// <summary>
/// Applies a Pending Update List (PUL) to a node store.
/// Per XQuery Update Facility spec §3.4.1, updates are applied
/// atomically in a specific order to prevent conflicts.
/// </summary>
public static class PendingUpdateApplicator
{
    /// <summary>The namespace of PhoeniXML's own error codes.</summary>
    internal const string PhxErrorNamespace = "https://schemas.phoenixml.dev/2026/functions";

    /// <summary>
    /// Applies the pending updates of a query when it ends (XQUF 3.0 §2.5: upd:applyUpdates is
    /// implicitly invoked), to the store of each target node. Every target is checked first: a
    /// node of a store that cannot be updated (one that does not implement
    /// <see cref="IUpdatableNodeStore"/>, such as a database's stored documents) is
    /// <c>phx:XUDY9001</c>, and nothing is changed. The list is empty afterwards.
    /// </summary>
    internal static void ApplyAtEnd(PendingUpdateList pul, QueryExecutionContext context)
    {
        if (!pul.HasUpdates)
            return;
        RequireCompatible(pul);
        var byStore = new List<(IUpdatableNodeStore Store, PendingUpdateList Updates)>();
        foreach (var primitive in pul.Primitives)
        {
            var target = primitive.Target as XdmNode;
            var store = target?.Store switch
            {
                IUpdatableNodeStore own => own,
                null => context.NodeStore as IUpdatableNodeStore,
                _ => null,
            };
            if (store is null)
            {
                throw new XQueryRuntimeException("XUDY9001",
                    $"The target of an update{(target is null ? "" : $" ({target.NodeKind} node)")} is in a store that cannot be "
                    + $"updated ({(target?.Store ?? (object?)context.NodeStore)?.GetType().Name ?? "none"}); nothing was changed")
                {
                    ErrorNamespaceUri = PhxErrorNamespace,
                    ErrorPrefix = "phx",
                };
            }
            var at = byStore.FindIndex(g => ReferenceEquals(g.Store, store));
            if (at < 0)
            {
                byStore.Add((store, new PendingUpdateList()));
                at = byStore.Count - 1;
            }
            byStore[at].Updates.Add(primitive);
        }
        pul.Clear();
        foreach (var (store, updates) in byStore)
            Apply(updates, store);
    }

    /// <summary>
    /// The updates of one list must not conflict (XQUF 3.0 upd:mergeUpdates / upd:applyUpdates):
    /// two renames of one node are XUDY0015, two replacements of one node XUDY0016, two
    /// replacements of one node's value (or content) XUDY0017. Without the check the last
    /// rename won, the first replacement won, and the last value won, silently (BUGS.md #128).
    /// </summary>
    internal static void RequireCompatible(PendingUpdateList pul)
    {
        var seen = new HashSet<(Type Kind, object? Store, NodeId Id)>();
        foreach (var primitive in pul.Primitives)
        {
            var code = primitive switch
            {
                RenamePrimitive => "XUDY0015",
                ReplaceNodePrimitive => "XUDY0016",
                ReplaceValuePrimitive => "XUDY0017",
                _ => null,
            };
            if (code is null || primitive.Target is not XdmNode target)
                continue;
            if (!seen.Add((primitive.GetType(), target.Store, target.Id)))
            {
                var what = code switch
                {
                    "XUDY0015" => "renamed",
                    "XUDY0016" => "replaced",
                    _ => "given a new value",
                };
                throw new XQueryRuntimeException(code, $"One {target.NodeKind} node is {what} twice by one query or modify clause");
            }
        }
    }

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

        // A store that defers work until the whole list is applied (string values) does it now,
        // whoever applies the list.
        (store as IUpdateBatch)?.UpdatesApplied();
        return applied;
    }

    private static bool ApplyInsert(InsertPrimitive prim, IUpdatableNodeStore store)
    {
        if (prim.Target is not XdmNode target) return false;
        var sourceNodes = GetSourceNodes(prim.Source);
        if (sourceNodes.Count == 0) return false;

        // Attributes of the insertion sequence go to an element's attributes, not its children
        // (upd:insertAttributes): the target for into / as first / as last, the target's parent
        // for before / after. BUGS.md #126.
        if (store is IAttributeUpdates attributeStore && sourceNodes.Any(n => n is XdmAttribute))
        {
            var owner = prim.Position is InsertPosition.Before or InsertPosition.After ? target.Parent : target.Id;
            if (owner is { } ownerId)
                attributeStore.InsertAttributes(ownerId, sourceNodes.OfType<XdmAttribute>().ToList());
            sourceNodes = sourceNodes.Where(n => n is not XdmAttribute).ToList();
            if (sourceNodes.Count == 0) return true;
        }

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

        // An empty replacement is allowed (upd:replaceNode takes "zero or more" nodes): the
        // target is removed. It used to be treated as nothing to do.
        var replacements = GetSourceNodes(prim.Replacement);

        // An attribute is not among its parent's children: replace it in the parent's
        // attributes. BUGS.md #126.
        if (target is XdmAttribute && store is IAttributeUpdates attributeStore)
            return attributeStore.ReplaceAttribute(target.Parent.Value, target.Id, replacements.OfType<XdmAttribute>().ToList());

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
        if (store is IAttributeUpdates attributeStore)
            attributeStore.RenameNode(target.Id, prim.NewName.LocalName, prim.NewName.Namespace, prim.NewName.Prefix);
        else
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
