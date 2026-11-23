using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// A node equal to another but for one change, for a store that applies XQuery updates by
/// replacing node objects: a node's name, value, children and attributes are init-only. The copy
/// keeps the node's id, so navigation, which finds nodes by id, sees it in the node's place, and
/// everything else the node carries: its parent, document, source position, tree ordinal, base URI
/// and store.
/// </summary>
internal static class NodeReplacement
{
    public static XdmElement Element(XdmElement e, IReadOnlyList<NodeId>? children = null,
        IReadOnlyList<NodeId>? attributes = null, IReadOnlyList<NamespaceBinding>? declarations = null)
        => Keep(e, new XdmElement
        {
            Id = e.Id,
            Document = e.Document,
            SourceLine = e.SourceLine,
            SourceColumn = e.SourceColumn,
            StringValueResolver = e.StringValueResolver,
            Namespace = e.Namespace,
            LocalName = e.LocalName,
            Prefix = e.Prefix,
            Attributes = attributes ?? e.Attributes,
            Children = children ?? e.Children,
            NamespaceDeclarations = declarations ?? e.NamespaceDeclarations,
            TypeAnnotation = e.TypeAnnotation,
            IsIdContent = e.IsIdContent,
        });

    public static XdmDocument Document(XdmDocument d, IReadOnlyList<NodeId> children, NodeId? documentElement,
        string? documentElementLocalName)
        => Keep(d, new XdmDocument
        {
            Id = d.Id,
            Document = d.Document,
            SourceLine = d.SourceLine,
            SourceColumn = d.SourceColumn,
            StringValueResolver = d.StringValueResolver,
            Children = children,
            DocumentElement = documentElement,
            DocumentUri = d.DocumentUri,
            DocumentElementLocalName = documentElementLocalName,
        });

    /// <summary>
    /// The node with a new value (upd:replaceValue): a text, comment or processing instruction's
    /// content, or an attribute's value. An attribute is then of type xs:untypedAtomic, and neither
    /// an ID nor an IDREF. Null for any other node.
    /// </summary>
    public static XdmNode? WithValue(XdmNode node, string value) => node switch
    {
        XdmText t => Keep(t, new XdmText
        {
            Id = t.Id, Document = t.Document, SourceLine = t.SourceLine, SourceColumn = t.SourceColumn,
            StringValueResolver = t.StringValueResolver, Value = value,
        }),
        XdmAttribute a => Keep(a, new XdmAttribute
        {
            Id = a.Id, Document = a.Document, SourceLine = a.SourceLine, SourceColumn = a.SourceColumn,
            StringValueResolver = a.StringValueResolver,
            Namespace = a.Namespace, LocalName = a.LocalName, Prefix = a.Prefix, Value = value,
        }),
        XdmComment c => Keep(c, new XdmComment
        {
            Id = c.Id, Document = c.Document, SourceLine = c.SourceLine, SourceColumn = c.SourceColumn,
            StringValueResolver = c.StringValueResolver, Value = value,
        }),
        XdmProcessingInstruction p => Keep(p, new XdmProcessingInstruction
        {
            Id = p.Id, Document = p.Document, SourceLine = p.SourceLine, SourceColumn = p.SourceColumn,
            StringValueResolver = p.StringValueResolver, Target = p.Target, Value = value,
        }),
        _ => null,
    };

    /// <summary>The node with a new name (upd:rename): an element, an attribute, or a processing instruction's target. Null for any other node.</summary>
    public static XdmNode? Renamed(XdmNode node, NamespaceId ns, string localName, string? prefix,
        IReadOnlyList<NamespaceBinding>? declarations = null) => node switch
    {
        XdmElement e => Keep(e, new XdmElement
        {
            Id = e.Id, Document = e.Document, SourceLine = e.SourceLine, SourceColumn = e.SourceColumn,
            StringValueResolver = e.StringValueResolver,
            Namespace = ns, LocalName = localName, Prefix = prefix,
            Attributes = e.Attributes, Children = e.Children,
            NamespaceDeclarations = declarations ?? e.NamespaceDeclarations,
            TypeAnnotation = e.TypeAnnotation, IsIdContent = e.IsIdContent,
        }),
        XdmAttribute a => Keep(a, new XdmAttribute
        {
            Id = a.Id, Document = a.Document, SourceLine = a.SourceLine, SourceColumn = a.SourceColumn,
            StringValueResolver = a.StringValueResolver,
            Namespace = ns, LocalName = localName, Prefix = prefix, Value = a.Value,
            TypeAnnotation = a.TypeAnnotation, IsId = a.IsId, IsIdRef = a.IsIdRef,
        }),
        XdmProcessingInstruction p => Keep(p, new XdmProcessingInstruction
        {
            Id = p.Id, Document = p.Document, SourceLine = p.SourceLine, SourceColumn = p.SourceColumn,
            StringValueResolver = p.StringValueResolver, Target = localName, Value = p.Value,
        }),
        _ => null,
    };

    private static T Keep<T>(XdmNode from, T to) where T : XdmNode
    {
        to.TreeOrdinal = from.TreeOrdinal;
        to.Parent = from.Parent;
        to.BaseUri = from.BaseUri;
        to.CopySourceBaseUri = from.CopySourceBaseUri;
        if (from.Store is { } store)
            to.AttachStore(store);
        return to;
    }
}
