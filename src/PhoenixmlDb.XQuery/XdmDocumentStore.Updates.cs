using System.Runtime.CompilerServices;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// XQuery Update on the documents and constructed nodes of this store (xquery#127): a query's
/// pending updates are applied to them when it ends, at every entry point. They used to be
/// dropped, or refused with XUST0002, so <c>insert node &lt;x/&gt; into $doc/r</c> changed nothing.
/// </summary>
/// <remarks>
/// A node's name, value, children and attributes are init-only, so a change replaces the node by
/// an equal one with the same id (<see cref="NodeReplacement"/>); navigation finds nodes by id
/// and sees the replacement. An element or document node whose children or attributes change is
/// replaced once by one whose list this store owns, and that list is then changed in place.
/// </remarks>
public sealed partial class XdmDocumentStore : IUpdatableNodeStore, IAttributeUpdates, IUpdateBatch
{
    // The child and attribute lists of replaced nodes, which this store may change in place.
    private readonly ConditionalWeakTable<List<NodeId>, object> _ownedLists = new();

    XdmNode? IUpdatableNodeStore.GetNode(NodeId id) => GetNode(id);

    NodeId IUpdatableNodeStore.AddNode(XdmNode node)
    {
        if (!ReferenceEquals(node.Store, this) || !_nodes.ContainsKey(node.Id))
            RegisterNode(node);
        return node.Id;
    }

    void IUpdatableNodeStore.RemoveNode(NodeId id) => _nodes.Remove(id);

    void IUpdatableNodeStore.InsertChild(NodeId parent, NodeId child, int position)
    {
        if (OwnChildren(parent) is not { } list)
            return;
        list.Insert(Math.Clamp(position, 0, list.Count), child);
        ChildrenChanged(parent);
    }

    void IUpdatableNodeStore.AppendChild(NodeId parent, NodeId child)
    {
        if (OwnChildren(parent) is not { } list)
            return;
        list.Add(child);
        ChildrenChanged(parent);
    }

    void IUpdatableNodeStore.RemoveChild(NodeId parent, NodeId child)
    {
        if (OwnChildren(parent) is not { } list)
            return;
        list.Remove(child);
        ChildrenChanged(parent);
    }

    IReadOnlyList<NodeId> IUpdatableNodeStore.GetChildren(NodeId parent) => _nodes.GetValueOrDefault(parent) switch
    {
        XdmElement e => e.Children,
        XdmDocument d => d.Children,
        _ => Array.Empty<NodeId>(),
    };

    void IUpdatableNodeStore.SetValue(NodeId node, string value)
    {
        switch (_nodes.GetValueOrDefault(node))
        {
            case XdmElement element:
                // upd:replaceElementContent: the children are one text node with the value, or none.
                if (OwnChildren(node) is not { } children)
                    return;
                foreach (var child in children)
                    _nodes.Remove(child);
                children.Clear();
                if (value.Length > 0)
                {
                    var text = new XdmText { Id = AllocateNodeId(), Document = element.Document, Value = value };
                    text.Parent = node;
                    RegisterNode(text);
                    children.Add(text.Id);
                }
                ChildrenChanged(node);
                break;
            case { } other when NodeReplacement.WithValue(other, value) is { } replaced:
                _nodes[node] = replaced;
                InvalidateStringValues(replaced.Parent);
                break;
        }
    }

    void IUpdatableNodeStore.RenameNode(NodeId node, string localName, NamespaceId ns)
        => Rename(node, localName, ns, newPrefix: null, keepPrefix: true);

    void IAttributeUpdates.RenameNode(NodeId node, string localName, NamespaceId ns, string? newPrefix)
        => Rename(node, localName, ns, string.IsNullOrEmpty(newPrefix) ? null : newPrefix, keepPrefix: false);

    private void Rename(NodeId node, string localName, NamespaceId ns, string? newPrefix, bool keepPrefix)
    {
        if (_nodes.GetValueOrDefault(node) is not { } existing)
            return;
        var prefix = keepPrefix ? existing switch
        {
            XdmElement e => e.Prefix,
            XdmAttribute a => a.Prefix,
            _ => null,
        } : newPrefix;
        // An element renamed into a namespace by a prefix it does not declare declares it, so the
        // name keeps its namespace when it is written.
        IReadOnlyList<NamespaceBinding>? declarations = null;
        if (existing is XdmElement element && prefix is not null
            && !element.NamespaceDeclarations.Any(b => b.Prefix == prefix && b.Namespace == ns))
            declarations = [.. element.NamespaceDeclarations.Where(b => b.Prefix != prefix), new NamespaceBinding(prefix, ns)];
        if (NodeReplacement.Renamed(existing, ns, localName, prefix, declarations) is { } renamed)
            _nodes[node] = renamed;
    }

    void IAttributeUpdates.InsertAttributes(NodeId element, IReadOnlyList<XdmAttribute> attributes)
    {
        if (OwnAttributes(element) is not { } list)
            return;
        foreach (var attribute in attributes)
        {
            attribute.Parent = element;
            if (!ReferenceEquals(attribute.Store, this) || !_nodes.ContainsKey(attribute.Id))
                RegisterNode(attribute);
            list.Add(attribute.Id);
        }
    }

    bool IAttributeUpdates.ReplaceAttribute(NodeId element, NodeId target, IReadOnlyList<XdmAttribute> replacements)
    {
        var list = OwnAttributes(element);
        var at = list?.IndexOf(target) ?? -1;
        if (at < 0)
            return false;
        list!.RemoveAt(at);
        _nodes.Remove(target);
        foreach (var attribute in replacements)
        {
            attribute.Parent = element;
            if (!ReferenceEquals(attribute.Store, this) || !_nodes.ContainsKey(attribute.Id))
                RegisterNode(attribute);
            list.Insert(at++, attribute.Id);
        }
        return true;
    }

    private bool Owned(IReadOnlyList<NodeId> list) => list is List<NodeId> own && _ownedLists.TryGetValue(own, out _);

    private List<NodeId> NewOwnedList(IReadOnlyList<NodeId> from)
    {
        var list = new List<NodeId>(from);
        _ownedLists.Add(list, list);
        return list;
    }

    // The children of an element or document node, as a list this store owns.
    private List<NodeId>? OwnChildren(NodeId parent)
    {
        switch (_nodes.GetValueOrDefault(parent))
        {
            case XdmElement e when Owned(e.Children):
                return (List<NodeId>)e.Children;
            case XdmElement e:
                var elementList = NewOwnedList(e.Children);
                _nodes[parent] = NodeReplacement.Element(e, children: elementList);
                return elementList;
            case XdmDocument d when Owned(d.Children):
                return (List<NodeId>)d.Children;
            case XdmDocument d:
                var documentList = NewOwnedList(d.Children);
                ReplaceDocument(d, NodeReplacement.Document(d, documentList, d.DocumentElement, d.DocumentElementLocalName));
                return documentList;
            default:
                return null;
        }
    }

    // The attributes of an element, as a list this store owns.
    private List<NodeId>? OwnAttributes(NodeId element)
    {
        if (_nodes.GetValueOrDefault(element) is not XdmElement e)
            return null;
        if (Owned(e.Attributes))
            return (List<NodeId>)e.Attributes;
        var list = NewOwnedList(e.Attributes);
        _nodes[element] = NodeReplacement.Element(e, attributes: list);
        return list;
    }

    // After a change of a node's children: a document node's document element is its first
    // element child, and the string values of the node and its ancestors are computed again.
    private void ChildrenChanged(NodeId parent)
    {
        if (_nodes.GetValueOrDefault(parent) is XdmDocument d)
        {
            var first = d.Children.FirstOrDefault(c => _nodes.GetValueOrDefault(c) is XdmElement);
            var element = first == default ? null : _nodes.GetValueOrDefault(first) as XdmElement;
            if (d.DocumentElement != element?.Id)
                ReplaceDocument(d, NodeReplacement.Document(d, d.Children, element?.Id, element?.LocalName));
        }
        InvalidateStringValues(parent);
    }

    // A replaced document node is also the one this store gives for its URI.
    private void ReplaceDocument(XdmDocument old, XdmDocument replacement)
    {
        _nodes[old.Id] = replacement;
        foreach (var uri in _documentsByUri.Where(kv => ReferenceEquals(kv.Value, old)).Select(kv => kv.Key).ToList())
            _documentsByUri[uri] = replacement;
        var at = _allDocuments.IndexOf(old);
        if (at >= 0)
            _allDocuments[at] = replacement;
    }

    // The nodes whose string value a change made stale: a changed node and its ancestors. Their
    // values are computed again once the whole update list is applied (UpdatesApplied), deepest
    // first, each from its children: computed again at each change, the whole document's text
    // was read once per inserted node, and a case of many inserts did not end.
    private readonly HashSet<NodeId> _staleStringValues = [];

    private void InvalidateStringValues(NodeId? id)
    {
        while (id is { } current && _staleStringValues.Add(current) && _nodes.GetValueOrDefault(current) is { } node)
            id = node.Parent;
    }

    void IUpdateBatch.UpdatesApplied()
    {
        if (_staleStringValues.Count == 0)
            return;
        var stale = _staleStringValues
            .Select(id => (Id: id, Depth: Depth(id)))
            .OrderByDescending(n => n.Depth)
            .ToList();
        _staleStringValues.Clear();
        foreach (var (id, _) in stale)
        {
            switch (_nodes.GetValueOrDefault(id))
            {
                // A parsed node has no resolver: its value was computed when it was read, and an
                // emptied one would read as "" (or throw under StrictStringValue).
                case XdmElement e:
                    e._stringValue = e.StringValueResolver is null ? TextOf(e.Children) : null;
                    break;
                case XdmDocument d:
                    d._stringValue = d.StringValueResolver is null ? TextOf(d.Children) : null;
                    break;
            }
        }
    }

    private int Depth(NodeId id)
    {
        var depth = 0;
        for (var node = _nodes.GetValueOrDefault(id); node?.Parent is { } parent && depth < 100_000; node = _nodes.GetValueOrDefault(parent))
            depth++;
        return depth;
    }

    // The text of the children: a text child's value, an element child's string value (computed
    // already when it was stale: the deepest go first).
    private string TextOf(IReadOnlyList<NodeId> children)
    {
        var text = new System.Text.StringBuilder();
        foreach (var child in children)
        {
            switch (_nodes.GetValueOrDefault(child))
            {
                case XdmText t:
                    text.Append(t.Value);
                    break;
                case XdmElement e:
                    text.Append(e.StringValue);
                    break;
            }
        }
        return text.ToString();
    }
}
