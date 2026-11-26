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
/// copy. A host that loads such a document into an <see cref="XdmDocumentStore"/> marks it with
/// <see cref="XdmDocumentStore.MarkReadOnly"/>, to the same effect for that document.
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
    /// <see cref="IUpdatableNodeStore"/>, such as a database's stored documents), or of a document
    /// marked read-only (<see cref="XdmDocumentStore.MarkReadOnly"/>), is <c>phx:XUDY9001</c>,
    /// and nothing is changed. The list is empty afterwards.
    /// </summary>
    internal static void ApplyAtEnd(PendingUpdateList pul, QueryExecutionContext context)
    {
        if (!pul.HasUpdates)
            return;
        RequireCompatible(pul);
        var byStore = new List<(IUpdatableNodeStore Store, PendingUpdateList Updates)>();
        var puts = new List<PutPrimitive>();
        foreach (var primitive in pul.Primitives)
        {
            // upd:put changes no node; it is applied last (XQUF 3.0 §8.2.3).
            if (primitive is PutPrimitive put)
            {
                puts.Add(put);
                continue;
            }
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
            // A document the host marked read-only (XdmDocumentStore.MarkReadOnly), such as a
            // database's stored document loaded for a query: the store can be updated, its
            // document cannot.
            if (target is not null && store is XdmDocumentStore documents
                && documents.ReadOnlyDocumentOf(target) is { } readOnly)
            {
                throw new XQueryRuntimeException("XUDY9001",
                    $"The target of an update ({target.NodeKind} node) is in a read-only document"
                    + $"{(readOnly.DocumentUri is { } uri ? $" ({uri})" : "")}; nothing was changed")
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
        foreach (var put in puts)
            ApplyPut(put, context);
    }

    /// <summary>
    /// upd:put: the node, as the other updates of the list left it, serialized with the put's
    /// parameters, stored at the URI fn:put authorised.
    /// </summary>
    private static void ApplyPut(PutPrimitive put, QueryExecutionContext context)
    {
        if (!put.Uri.IsFile)
            throw Functions.PutFunction.Refused($"fn:put can store only to a file, not to '{put.Uri}'.");
        // The node as its store has it now: the object fn:put was given is the node before the
        // other updates of the list were applied.
        var node = put.Target is XdmNode given && given.Store?.GetNode(given.Id) is { } current ? current : put.Target;
        var text = (string?)new Functions.Serialize2Function().InvokeAsync([node, put.Parameters], context)
            .AsTask().GetAwaiter().GetResult() ?? "";
        try
        {
            Security.AuthorizedFile.WriteAllText(put.Uri, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Security.ResourceAccessDeniedException)
        {
            throw Functions.PutFunction.Refused($"fn:put could not store the node at '{put.Uri}': {e.Message}", e);
        }
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
        RequireDistinctAttributeNames(pul);
        RequireCompatibleNamespaceBindings(pul);

        // Two puts to one URI (XQUF 3.0 §5.8, upd:mergeUpdates): the URIs fn:put authorised are
        // absolute, and for a file canonical.
        var putUris = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var put in pul.Primitives.OfType<PutPrimitive>())
        {
            if (!putUris.Add(put.Uri.AbsoluteUri))
                throw new XQueryRuntimeException("XUDY0031", $"fn:put is called twice for '{put.Uri}' in one query");
        }
    }

    /// <summary>
    /// The namespace bindings the updates of one list imply on one element must agree (XQUF 3.0
    /// upd:applyUpdates, XUDY0024): attributes inserted or replaced into it, and its new name or the
    /// new name of one of its attributes. Each expression checks its own against the element's
    /// namespaces (XUDY0023); two expressions could still bind one prefix to two URIs.
    /// </summary>
    private static void RequireCompatibleNamespaceBindings(PendingUpdateList pul)
    {
        Dictionary<(object? Store, NodeId Id), Dictionary<string, string>>? bindings = null;

        void Bind(XdmElement? owner, (string Prefix, string Uri)? binding)
        {
            if (owner is null || binding is not { } b)
                return;
            bindings ??= [];
            var key = ((object?)owner.Store, owner.Id);
            if (!bindings.TryGetValue(key, out var onElement))
                bindings[key] = onElement = new Dictionary<string, string>(StringComparer.Ordinal);
            if (onElement.TryGetValue(b.Prefix, out var other) && other != b.Uri)
                throw UpdateTargets.Conflict("XUDY0024", "The updates", b, other);
            onElement[b.Prefix] = b.Uri;
        }

        static XdmElement? Parent(XdmNode node)
            => node.Parent is { } parent && parent != NodeId.None ? node.Store?.GetNode(parent) as XdmElement : null;

        foreach (var primitive in pul.Primitives)
        {
            switch (primitive)
            {
                case InsertPrimitive insert when insert.Target is XdmNode target:
                    var owner = insert.Position is InsertPosition.Before or InsertPosition.After ? Parent(target) : target as XdmElement;
                    foreach (var attribute in GetSourceNodes(insert.Source).OfType<XdmAttribute>())
                        Bind(owner, UpdateTargets.ImpliedBinding(attribute));
                    break;
                case ReplaceNodePrimitive replace when replace.Target is XdmAttribute attribute:
                    foreach (var replacement in GetSourceNodes(replace.Replacement).OfType<XdmAttribute>())
                        Bind(Parent(attribute), UpdateTargets.ImpliedBinding(replacement));
                    break;
                case RenamePrimitive rename when rename.Target is XdmElement or XdmAttribute:
                    var node = (XdmNode)rename.Target;
                    var uri = rename.NewName.ResolvedNamespace
                        ?? (rename.NewName.Namespace == NamespaceId.None ? "" : node.Store?.GetNamespaceUri(rename.NewName.Namespace) ?? "");
                    Bind(node as XdmElement ?? Parent(node), UpdateTargets.ImpliedBinding(rename.NewName.Prefix, uri, node is XdmElement));
                    break;
            }
        }
    }

    /// <summary>
    /// The XDM instance the updates give must be valid (XQUF 3.0 upd:applyUpdates, XUDY0021): an
    /// element may not end with two attributes of the same name. Checked on the whole list before
    /// anything changes; the second attribute was added beside the first.
    /// </summary>
    private static void RequireDistinctAttributeNames(PendingUpdateList pul)
    {
        Dictionary<(object? Store, NodeId Id), (XdmElement Element, HashSet<NodeId> Removed, List<(string Uri, string Local)> Added)>? changes = null;

        void Change(XdmElement? owner, XdmAttribute? removed, IEnumerable<(string Uri, string Local)> added)
        {
            if (owner is null)
                return;
            changes ??= [];
            var key = ((object?)owner.Store, owner.Id);
            if (!changes.TryGetValue(key, out var change))
                changes[key] = change = (owner, [], []);
            if (removed is not null)
                change.Removed.Add(removed.Id);
            change.Added.AddRange(added);
        }

        foreach (var primitive in pul.Primitives)
        {
            switch (primitive)
            {
                case InsertPrimitive insert when insert.Target is XdmNode target:
                    var attributes = GetSourceNodes(insert.Source).OfType<XdmAttribute>().ToList();
                    if (attributes.Count > 0)
                        Change(insert.Position is InsertPosition.Before or InsertPosition.After ? ParentElement(target) : target as XdmElement,
                            null, attributes.Select(NameOf));
                    break;
                case ReplaceNodePrimitive replace when replace.Target is XdmAttribute attribute:
                    Change(ParentElement(attribute), attribute,
                        GetSourceNodes(replace.Replacement).OfType<XdmAttribute>().Select(NameOf));
                    break;
                case RenamePrimitive rename when rename.Target is XdmAttribute attribute:
                    var uri = rename.NewName.ResolvedNamespace
                        ?? (rename.NewName.Namespace == NamespaceId.None ? "" : attribute.Store?.GetNamespaceUri(rename.NewName.Namespace) ?? "");
                    Change(ParentElement(attribute), attribute, [(uri, rename.NewName.LocalName)]);
                    break;
                case DeletePrimitive delete when delete.Target is XdmAttribute attribute:
                    Change(ParentElement(attribute), attribute, []);
                    break;
            }
        }
        if (changes is null)
            return;

        foreach (var (element, removed, added) in changes.Values)
        {
            var names = new HashSet<(string Uri, string Local)>();
            foreach (var id in element.Attributes)
            {
                if (!removed.Contains(id) && element.Store?.GetNode(id) is XdmAttribute existing)
                    names.Add(NameOf(existing));
            }
            foreach (var name in added)
            {
                if (!names.Add(name))
                    throw new XQueryRuntimeException("XUDY0021",
                        $"The updates would give the element {element.LocalName} two attributes named "
                        + $"{(name.Uri.Length == 0 ? name.Local : $"Q{{{name.Uri}}}{name.Local}")}; nothing was changed");
            }
        }

        static (string Uri, string Local) NameOf(XdmAttribute attribute)
            => (attribute.Namespace == NamespaceId.None ? "" : attribute.Store?.GetNamespaceUri(attribute.Namespace) ?? "", attribute.LocalName);

        static XdmElement? ParentElement(XdmNode node)
            => node.Parent is { } parent && parent != NodeId.None ? node.Store?.GetNode(parent) as XdmElement : null;
    }

    /// <summary>
    /// Applies a PUL to a node store. Returns the number of primitives applied.
    /// </summary>
    public static int Apply(PendingUpdateList pul, IUpdatableNodeStore store)
    {
        if (!pul.HasUpdates) return 0;
        var applied = new HashSet<UpdatePrimitive>(ReferenceEqualityComparer.Instance);
        var inserts = pul.Primitives.OfType<InsertPrimitive>().ToList();
        var replaceValues = pul.Primitives.OfType<ReplaceValuePrimitive>().ToList();

        // XQUF 3.0 §8.2.3 upd:applyUpdates, in its order. The inserts were applied in the order of
        // the list, so a node inserted as first or as last, or before or after a child, came before
        // a node inserted into the same element by an earlier expression; and the content of an
        // element was replaced before the nodes were inserted into it.
        // 1. upd:insertInto, upd:insertAttributes, upd:replaceValue, upd:rename.
        foreach (var prim in inserts)
        {
            if (ApplyInsertedAttributes(prim, store)) applied.Add(prim);
            if (prim.Position == InsertPosition.Into && ApplyInsert(prim, store)) applied.Add(prim);
        }
        foreach (var prim in replaceValues)
        {
            if (prim.Target is not XdmElement && ApplyReplaceValue(prim, store)) applied.Add(prim);
        }
        foreach (var prim in pul.Primitives.OfType<RenamePrimitive>())
        {
            if (ApplyRename(prim, store)) applied.Add(prim);
        }

        // 2. upd:insertBefore, upd:insertAfter, upd:insertIntoAsFirst, upd:insertIntoAsLast.
        foreach (var prim in inserts)
        {
            if (prim.Position != InsertPosition.Into && ApplyInsert(prim, store)) applied.Add(prim);
        }

        // 3. upd:replaceNode.
        foreach (var prim in pul.Primitives.OfType<ReplaceNodePrimitive>())
        {
            if (ApplyReplaceNode(prim, store)) applied.Add(prim);
        }

        // 4. upd:replaceElementContent (replace value of node of an element).
        foreach (var prim in replaceValues)
        {
            if (prim.Target is XdmElement && ApplyReplaceValue(prim, store)) applied.Add(prim);
        }

        // 5. upd:delete.
        foreach (var prim in pul.Primitives.OfType<DeletePrimitive>())
        {
            if (ApplyDelete(prim, store)) applied.Add(prim);
        }

        // A store that defers work until the whole list is applied (string values) does it now,
        // whoever applies the list.
        (store as IUpdateBatch)?.UpdatesApplied();
        return applied.Count;
    }

    /// <summary>
    /// The attributes of an insertion go to an element's attributes, not its children
    /// (upd:insertAttributes, phase 1): the target for into / as first / as last, the target's
    /// parent for before / after. BUGS.md #126.
    /// </summary>
    private static bool ApplyInsertedAttributes(InsertPrimitive prim, IUpdatableNodeStore store)
    {
        if (prim.Target is not XdmNode target || store is not IAttributeUpdates attributeStore)
            return false;
        var attributes = GetSourceNodes(prim.Source).OfType<XdmAttribute>().ToList();
        if (attributes.Count == 0)
            return false;
        var owner = prim.Position is InsertPosition.Before or InsertPosition.After ? target.Parent : target.Id;
        if (owner is not { } ownerId)
            return false;
        attributeStore.InsertAttributes(ownerId, attributes);
        return true;
    }

    /// <summary>The nodes of an insertion that are not attributes, as children (or siblings) of the target.</summary>
    private static bool ApplyInsert(InsertPrimitive prim, IUpdatableNodeStore store)
    {
        if (prim.Target is not XdmNode target) return false;
        var sourceNodes = GetSourceNodes(prim.Source);
        // A store that takes attributes separately had them in phase 1.
        if (store is IAttributeUpdates)
            sourceNodes = sourceNodes.Where(n => n is not XdmAttribute).ToList();
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
