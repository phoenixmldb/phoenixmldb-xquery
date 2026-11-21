using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Brings a subtree of another node store into a store a constructor builds in, so that the
/// constructor's own copy code, which reads ids in its store, can copy it (xslt BUGS.md #124).
/// </summary>
/// <remarks>
/// <para>
/// A node of another store (one passed in from another query or transformation) has children,
/// attributes and a parent that are ids of THAT store, and namespace ids of that store. Copying
/// it with the target store's lookups read whatever target node had the same id, and kept
/// namespace ids that mean another URI here. The import walks the tree in the source store and
/// gives every node a new id in the target store, every namespace id through its URI.
/// </para>
/// <para>
/// The imported root carries the source root's in-scope namespaces (its own declarations and
/// those of its ancestors in the source store), because the copy that follows can no longer
/// reach those ancestors. Iterative, so a deep tree cannot exhaust the stack.
/// </para>
/// </remarks>
internal static class ForeignTreeImporter
{
    /// <summary>
    /// <paramref name="source"/> when it is not from another store than <paramref name="target"/>;
    /// otherwise its import into <paramref name="target"/>.
    /// </summary>
    internal static XdmNode ImportIfForeign(XdmNode source, INodeBuilder target)
    {
        if (source.Store is not { } from || ReferenceEquals(from, target))
            return source;
        return Import(source, from, target);
    }

    private static XdmNode Import(XdmNode root, IXdmNodeStore from, INodeBuilder to)
    {
        var namespaces = new Dictionary<NamespaceId, NamespaceId>();
        NamespaceId Map(NamespaceId id)
        {
            if (id == NamespaceId.None) return NamespaceId.None;
            if (!namespaces.TryGetValue(id, out var mapped))
            {
                var uri = from.GetNamespaceUri(id)
                    ?? throw new XQueryRuntimeException("FODC0002",
                        $"Node {root.Id} is from another node store, which does not know its namespace id {id.Value}.");
                namespaces[id] = mapped = to.InternNamespace(uri);
            }
            return mapped;
        }

        IReadOnlyList<NamespaceBinding> MapBindings(IReadOnlyList<NamespaceBinding>? bindings)
        {
            if (bindings is not { Count: > 0 }) return XdmElement.EmptyNamespaceDeclarations;
            var list = new List<NamespaceBinding>(bindings.Count);
            foreach (var b in bindings)
                list.Add(new NamespaceBinding(b.Prefix, Map(b.Namespace)));
            return list;
        }

        // The root's in-scope namespaces: nearest declaration of each prefix wins.
        IReadOnlyList<NamespaceBinding> InScope(XdmElement element)
        {
            var seen = new Dictionary<string, NamespaceBinding>(StringComparer.Ordinal);
            XdmNode? cursor = element;
            while (cursor != null)
            {
                if (cursor is XdmElement e && e.NamespaceDeclarations != null)
                {
                    foreach (var b in e.NamespaceDeclarations)
                        seen.TryAdd(b.Prefix, new NamespaceBinding(b.Prefix, Map(b.Namespace)));
                }
                cursor = cursor.Parent is { } p && p != NodeId.None ? from.GetNode(p) : null;
            }
            return seen.Count == 0 ? XdmElement.EmptyNamespaceDeclarations : seen.Values.ToList();
        }

        // Each node's id is allocated when it is pushed, so a document knows its element's id.
        var pending = new Stack<(XdmNode Source, NodeId Id, NodeId? Parent, List<NodeId>? ParentChildren)>();
        XdmNode? result = null;
        pending.Push((root, to.AllocateId(), null, null));
        while (pending.Count > 0)
        {
            var (source, id, parent, parentChildren) = pending.Pop();
            XdmNode copy;
            switch (source)
            {
                case XdmElement elem:
                {
                    var attributes = new List<NodeId>(elem.Attributes.Count);
                    var children = new List<NodeId>(elem.Children.Count);
                    var element = new XdmElement
                    {
                        Id = id,
                        Document = elem.Document,
                        Namespace = Map(elem.Namespace),
                        LocalName = elem.LocalName,
                        Prefix = elem.Prefix,
                        Attributes = attributes,
                        Children = children,
                        NamespaceDeclarations = parent == null ? InScope(elem) : MapBindings(elem.NamespaceDeclarations),
                        TypeAnnotation = elem.TypeAnnotation,
                    };
                    element._stringValue = elem._stringValue ?? elem.StringValue;
                    foreach (var attrId in elem.Attributes)
                    {
                        if (from.GetNode(attrId) is not XdmAttribute attr) continue;
                        var attrCopy = new XdmAttribute
                        {
                            Id = to.AllocateId(),
                            Document = attr.Document,
                            Namespace = Map(attr.Namespace),
                            LocalName = attr.LocalName,
                            Prefix = attr.Prefix,
                            Value = attr.Value,
                            TypeAnnotation = attr.TypeAnnotation,
                            IsId = attr.IsId,
                        };
                        attrCopy.Parent = id;
                        to.RegisterNode(attrCopy);
                        attributes.Add(attrCopy.Id);
                    }
                    // Children are pushed in reverse so they are created, and listed, in order.
                    for (var i = elem.Children.Count - 1; i >= 0; i--)
                    {
                        if (from.GetNode(elem.Children[i]) is { } child)
                            pending.Push((child, to.AllocateId(), id, children));
                    }
                    copy = element;
                    break;
                }
                case XdmDocument doc:
                {
                    var children = new List<NodeId>(doc.Children.Count);
                    var staged = new List<(XdmNode Child, NodeId Id)>(doc.Children.Count);
                    NodeId? documentElement = null;
                    foreach (var childId in doc.Children)
                    {
                        if (from.GetNode(childId) is not { } child) continue;
                        var childCopyId = to.AllocateId();
                        if (child is XdmElement && documentElement == null) documentElement = childCopyId;
                        staged.Add((child, childCopyId));
                    }
                    var document = new XdmDocument
                    {
                        Id = id,
                        Document = doc.Document,
                        Children = children,
                        DocumentElement = documentElement,
                        DocumentUri = doc.DocumentUri,
                    };
                    for (var i = staged.Count - 1; i >= 0; i--)
                        pending.Push((staged[i].Child, staged[i].Id, id, children));
                    copy = document;
                    break;
                }
                case XdmAttribute attr:
                    copy = new XdmAttribute
                    {
                        Id = id, Document = attr.Document, Namespace = Map(attr.Namespace), LocalName = attr.LocalName,
                        Prefix = attr.Prefix, Value = attr.Value, TypeAnnotation = attr.TypeAnnotation, IsId = attr.IsId,
                    };
                    break;
                case XdmText text:
                    copy = new XdmText { Id = id, Document = text.Document, Value = text.Value };
                    break;
                case XdmComment comment:
                    copy = new XdmComment { Id = id, Document = comment.Document, Value = comment.Value };
                    break;
                case XdmProcessingInstruction pi:
                    copy = new XdmProcessingInstruction { Id = id, Document = pi.Document, Target = pi.Target, Value = pi.Value };
                    break;
                default:
                    throw new XQueryRuntimeException("XPTY0004",
                        $"A {source.GetType().Name} from another node store cannot be copied.");
            }
            copy.BaseUri = source.BaseUri;
            copy.Parent = parent;
            to.RegisterNode(copy);
            parentChildren?.Add(id);
            result ??= copy;
        }
        return result!;
    }
}
