using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// In-memory implementation of <see cref="IUpdatableNodeStore"/> for XQuery Update Facility.
/// Wraps XDM nodes and maintains mutable children/attribute lists to support insert, delete,
/// replace, rename, and value-of operations.
/// </summary>
/// <remarks>
/// <para>
/// Because XDM node types use <c>required init</c> properties for <see cref="XdmElement.Children"/>
/// and <see cref="XdmElement.Attributes"/>, this store maintains a parallel mutable structure
/// (<see cref="_childrenOverrides"/>) that shadows the original immutable lists when mutations occur.
/// </para>
/// <para>
/// This implementation is designed for the <c>copy/modify/return</c> (transform) pattern where
/// nodes are deep-copied into this store, mutated via PUL application, and then returned as results.
/// It can also be used for standalone update execution against in-memory documents.
/// </para>
/// </remarks>
public sealed class InMemoryUpdatableNodeStore : IUpdatableNodeStore, INodeProvider
{
    private readonly Dictionary<NodeId, XdmNode> _nodes = new();
    private readonly Dictionary<NodeId, List<NodeId>> _childrenOverrides = new();
    private ulong _nextId;

    /// <summary>
    /// Creates a new empty store with node IDs starting at the given base.
    /// </summary>
    /// <param name="startId">The starting node ID value. Should be chosen to avoid
    /// collisions with existing node IDs in the query context.</param>
    public InMemoryUpdatableNodeStore(ulong startId = 1_000_000_000)
    {
        _nextId = startId;
    }

    /// <inheritdoc />
    public XdmNode? GetNode(NodeId id) => _nodes.GetValueOrDefault(id);

    /// <summary>
    /// Returns all nodes currently in the store.
    /// </summary>
    public IEnumerable<XdmNode> AllNodes => _nodes.Values;

    /// <inheritdoc />
    public NodeId AddNode(XdmNode node)
    {
        _nodes[node.Id] = node;
        return node.Id;
    }

    /// <inheritdoc />
    public void RemoveNode(NodeId id)
    {
        _nodes.Remove(id);
        _childrenOverrides.Remove(id);
    }

    /// <inheritdoc />
    public void InsertChild(NodeId parent, NodeId child, int position)
    {
        var children = GetMutableChildren(parent);
        if (position < 0) position = 0;
        if (position > children.Count) position = children.Count;
        children.Insert(position, child);
    }

    /// <inheritdoc />
    public void AppendChild(NodeId parent, NodeId child)
    {
        var children = GetMutableChildren(parent);
        children.Add(child);
    }

    /// <inheritdoc />
    public void RemoveChild(NodeId parent, NodeId child)
    {
        var children = GetMutableChildren(parent);
        children.Remove(child);
    }

    /// <inheritdoc />
    public IReadOnlyList<NodeId> GetChildren(NodeId parent)
    {
        if (_childrenOverrides.TryGetValue(parent, out var overridden))
            return overridden;

        if (_nodes.TryGetValue(parent, out var node))
        {
            return node switch
            {
                XdmElement e => e.Children,
                XdmDocument d => d.Children,
                _ => Array.Empty<NodeId>()
            };
        }

        return Array.Empty<NodeId>();
    }

    /// <inheritdoc />
    public void SetValue(NodeId node, string value)
    {
        if (!_nodes.TryGetValue(node, out var existing)) return;

        switch (existing)
        {
            case XdmText text:
                // XdmText.Value is init-only, so we replace the node entirely
                var newText = new XdmText
                {
                    Id = text.Id,
                    Document = text.Document,
                    Value = value
                };
                newText.Parent = text.Parent;
                _nodes[node] = newText;
                break;

            case XdmAttribute attr:
                var newAttr = new XdmAttribute
                {
                    Id = attr.Id,
                    Document = attr.Document,
                    Namespace = attr.Namespace,
                    LocalName = attr.LocalName,
                    Prefix = attr.Prefix,
                    Value = value,
                    TypeAnnotation = attr.TypeAnnotation,
                    IsId = attr.IsId
                };
                newAttr.Parent = attr.Parent;
                _nodes[node] = newAttr;
                break;

            case XdmComment comment:
                var newComment = new XdmComment
                {
                    Id = comment.Id,
                    Document = comment.Document,
                    Value = value
                };
                newComment.Parent = comment.Parent;
                _nodes[node] = newComment;
                break;

            case XdmProcessingInstruction pi:
                var newPi = new XdmProcessingInstruction
                {
                    Id = pi.Id,
                    Document = pi.Document,
                    Target = pi.Target,
                    Value = value
                };
                newPi.Parent = pi.Parent;
                _nodes[node] = newPi;
                break;

            case XdmElement elem:
                // Replace value of element = replace all text children with a single text node
                var elemChildren = GetMutableChildren(node);
                // Remove existing text children
                for (var i = elemChildren.Count - 1; i >= 0; i--)
                {
                    if (_nodes.TryGetValue(elemChildren[i], out var child) && child is XdmText)
                    {
                        _nodes.Remove(elemChildren[i]);
                        elemChildren.RemoveAt(i);
                    }
                }
                // Add new text node if value is non-empty
                if (!string.IsNullOrEmpty(value))
                {
                    var textId = AllocateId();
                    var textNode = new XdmText
                    {
                        Id = textId,
                        Document = elem.Document,
                        Value = value
                    };
                    textNode.Parent = node;
                    _nodes[textId] = textNode;
                    elemChildren.Add(textId);
                }
                break;
        }
    }

    /// <inheritdoc />
    public void RenameNode(NodeId node, string localName, NamespaceId ns)
    {
        if (!_nodes.TryGetValue(node, out var existing)) return;

        switch (existing)
        {
            case XdmElement elem:
                var newElem = new XdmElement
                {
                    Id = elem.Id,
                    Document = elem.Document,
                    Namespace = ns,
                    LocalName = localName,
                    Prefix = elem.Prefix,
                    Attributes = elem.Attributes,
                    Children = elem.Children, // original; overrides still tracked separately
                    NamespaceDeclarations = elem.NamespaceDeclarations,
                    TypeAnnotation = elem.TypeAnnotation
                };
                newElem.Parent = elem.Parent;
                _nodes[node] = newElem;
                break;

            case XdmAttribute attr:
                var newAttr = new XdmAttribute
                {
                    Id = attr.Id,
                    Document = attr.Document,
                    Namespace = ns,
                    LocalName = localName,
                    Prefix = attr.Prefix,
                    Value = attr.Value,
                    TypeAnnotation = attr.TypeAnnotation,
                    IsId = attr.IsId
                };
                newAttr.Parent = attr.Parent;
                _nodes[node] = newAttr;
                break;

            case XdmProcessingInstruction pi:
                var newPi = new XdmProcessingInstruction
                {
                    Id = pi.Id,
                    Document = pi.Document,
                    Target = localName, // PI target = local name
                    Value = pi.Value
                };
                newPi.Parent = pi.Parent;
                _nodes[node] = newPi;
                break;
        }
    }

    /// <summary>
    /// Allocates a new unique <see cref="NodeId"/> for constructed nodes.
    /// </summary>
    public NodeId AllocateId() => new NodeId(_nextId++);

    /// <summary>
    /// Deep-copies a node and all its descendants into this store, returning the copy's root ID.
    /// The copy has new node IDs and can be mutated without affecting the original.
    /// </summary>
    /// <param name="source">The root node to copy.</param>
    /// <param name="resolveNode">Function to resolve NodeIds to XdmNodes (from the source context).</param>
    /// <returns>The new root node of the deep copy.</returns>
    public XdmNode DeepCopy(XdmNode source, Func<NodeId, XdmNode?> resolveNode)
    {
        var idMap = new Dictionary<NodeId, NodeId>();
        return DeepCopyInternal(source, resolveNode, idMap, parentId: null);
    }

    private XdmNode DeepCopyInternal(
        XdmNode source,
        Func<NodeId, XdmNode?> resolveNode,
        Dictionary<NodeId, NodeId> idMap,
        NodeId? parentId)
    {
        var newId = AllocateId();
        idMap[source.Id] = newId;

        XdmNode copy;

        switch (source)
        {
            case XdmDocument doc:
            {
                var newChildren = new List<NodeId>();
                var newDoc = new XdmDocument
                {
                    Id = newId,
                    Document = new DocumentId(newId.Value), // new document identity
                    Children = newChildren,
                    DocumentElement = null, // set below
                    DocumentUri = null // copies don't retain document URI per spec
                };
                newDoc.Parent = null;
                _nodes[newId] = newDoc;

                NodeId? docElem = null;
                foreach (var childId in doc.Children)
                {
                    var childNode = resolveNode(childId);
                    if (childNode == null) continue;
                    var childCopy = DeepCopyInternal(childNode, resolveNode, idMap, newId);
                    newChildren.Add(childCopy.Id);
                    if (childCopy is XdmElement && docElem == null)
                        docElem = childCopy.Id;
                }

                // We can't set DocumentElement after init, but the children list is what matters
                // for traversal. The store's GetChildren will return the mutable list.
                _childrenOverrides[newId] = newChildren;
                copy = newDoc;
                break;
            }

            case XdmElement elem:
            {
                var newChildren = new List<NodeId>();
                var newAttrs = new List<NodeId>();

                var newElem = new XdmElement
                {
                    Id = newId,
                    Document = elem.Document,
                    Namespace = elem.Namespace,
                    LocalName = elem.LocalName,
                    Prefix = elem.Prefix,
                    Attributes = newAttrs,
                    Children = newChildren,
                    NamespaceDeclarations = elem.NamespaceDeclarations,
                    TypeAnnotation = elem.TypeAnnotation
                };
                newElem.Parent = parentId;
                // StringValue is lazily computed; no need to copy internal cache
                _nodes[newId] = newElem;

                // Copy attributes
                foreach (var attrId in elem.Attributes)
                {
                    var attrNode = resolveNode(attrId);
                    if (attrNode is XdmAttribute attr)
                    {
                        var newAttrId = AllocateId();
                        var newAttr = new XdmAttribute
                        {
                            Id = newAttrId,
                            Document = attr.Document,
                            Namespace = attr.Namespace,
                            LocalName = attr.LocalName,
                            Prefix = attr.Prefix,
                            Value = attr.Value,
                            TypeAnnotation = attr.TypeAnnotation,
                            IsId = attr.IsId
                        };
                        newAttr.Parent = newId;
                        _nodes[newAttrId] = newAttr;
                        newAttrs.Add(newAttrId);
                        idMap[attrId] = newAttrId;
                    }
                }

                // Copy children
                foreach (var childId in elem.Children)
                {
                    var childNode = resolveNode(childId);
                    if (childNode == null) continue;
                    var childCopy = DeepCopyInternal(childNode, resolveNode, idMap, newId);
                    newChildren.Add(childCopy.Id);
                }

                // Use the mutable lists directly (they're already set as the init values)
                // but also register overrides for future mutations
                _childrenOverrides[newId] = newChildren;
                copy = newElem;
                break;
            }

            case XdmText text:
            {
                var newText = new XdmText
                {
                    Id = newId,
                    Document = text.Document,
                    Value = text.Value
                };
                newText.Parent = parentId;
                _nodes[newId] = newText;
                copy = newText;
                break;
            }

            case XdmAttribute attr:
            {
                var newAttr = new XdmAttribute
                {
                    Id = newId,
                    Document = attr.Document,
                    Namespace = attr.Namespace,
                    LocalName = attr.LocalName,
                    Prefix = attr.Prefix,
                    Value = attr.Value,
                    TypeAnnotation = attr.TypeAnnotation,
                    IsId = attr.IsId
                };
                newAttr.Parent = parentId;
                _nodes[newId] = newAttr;
                copy = newAttr;
                break;
            }

            case XdmComment comment:
            {
                var newComment = new XdmComment
                {
                    Id = newId,
                    Document = comment.Document,
                    Value = comment.Value
                };
                newComment.Parent = parentId;
                _nodes[newId] = newComment;
                copy = newComment;
                break;
            }

            case XdmProcessingInstruction pi:
            {
                var newPi = new XdmProcessingInstruction
                {
                    Id = newId,
                    Document = pi.Document,
                    Target = pi.Target,
                    Value = pi.Value
                };
                newPi.Parent = parentId;
                _nodes[newId] = newPi;
                copy = newPi;
                break;
            }

            case XdmNamespace ns:
            {
                var newNs = new XdmNamespace
                {
                    Id = newId,
                    Document = ns.Document,
                    Prefix = ns.Prefix,
                    Uri = ns.Uri
                };
                newNs.Parent = parentId;
                _nodes[newId] = newNs;
                copy = newNs;
                break;
            }

            default:
                throw new InvalidOperationException($"Unknown node kind: {source.NodeKind}");
        }

        return copy;
    }

    /// <summary>
    /// Gets a mutable children list for a parent node, creating an override if needed.
    /// </summary>
    private List<NodeId> GetMutableChildren(NodeId parent)
    {
        if (_childrenOverrides.TryGetValue(parent, out var list))
            return list;

        // Create mutable copy from the node's immutable children
        var original = _nodes.TryGetValue(parent, out var node)
            ? node switch
            {
                XdmElement e => e.Children,
                XdmDocument d => d.Children,
                _ => (IReadOnlyList<NodeId>)Array.Empty<NodeId>()
            }
            : Array.Empty<NodeId>();

        list = new List<NodeId>(original);
        _childrenOverrides[parent] = list;
        return list;
    }
}
