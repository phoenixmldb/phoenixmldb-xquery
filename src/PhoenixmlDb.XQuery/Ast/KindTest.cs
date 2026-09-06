using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Tests node by kind (element(), text(), node(), etc.).
/// </summary>
public sealed class KindTest : NodeTest
{
    public required XdmNodeKind Kind { get; init; }

    /// <summary>
    /// Optional name for element(name) or attribute(name).
    /// </summary>
    public NameTest? Name { get; init; }

    /// <summary>
    /// Optional type for element(*, type) or attribute(*, type).
    /// </summary>
    public XdmTypeName? TypeName { get; init; }

    /// <summary>
    /// For document-node(element(E)): the element test that the document element must match.
    /// </summary>
    public NameTest? DocumentElementTest { get; init; }

    public override bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName)
    {
        // node() matches everything
        if (Kind == XdmNodeKind.None)
            return true;

        // Check kind
        if (kind != Kind)
            return false;

        // Check name if specified
        if (Name != null && !Name.Matches(kind, ns, localName))
            return false;

        return true;
    }

    public override string ToString()
    {
        var kindStr = Kind switch
        {
            XdmNodeKind.None => "node",
            XdmNodeKind.Document => "document-node",
            XdmNodeKind.Element => "element",
            XdmNodeKind.Attribute => "attribute",
            XdmNodeKind.Text => "text",
            XdmNodeKind.Comment => "comment",
            XdmNodeKind.ProcessingInstruction => "processing-instruction",
            XdmNodeKind.Namespace => "namespace-node",
            _ => "node"
        };

        if (Name != null)
            return $"{kindStr}({Name})";
        if (TypeName != null)
            return $"{kindStr}(*, {TypeName})";
        return $"{kindStr}()";
    }
}
