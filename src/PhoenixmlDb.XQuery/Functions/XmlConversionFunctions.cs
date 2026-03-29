using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

// ─── fn:parse-xml ──────────────────────────────────────────────────────────

/// <summary>
/// fn:parse-xml($arg as xs:string?) as document-node()?
/// Parses XML from a string and returns a document node.
/// </summary>
public sealed class ParseXmlFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-xml");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Document, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(null);
        var xmlStr = arg.ToString() ?? "";
        try
        {
            // Convert to XDM so XPath axis navigation works (e.g., $tree//e)
            if (context.NodeStore is INodeBuilder builder)
            {
                var xmlDoc = LoadXmlWithDtd(xmlStr);
                var xdmDoc = ConvertToXdm(xmlDoc, builder, documentUri: null);
                // Temporary trees have no document URI per XSLT 3.0 §11.9.1
                xdmDoc.DocumentUri = null;
                return ValueTask.FromResult<object?>(xdmDoc);
            }
            // Fallback to LINQ XDocument when no node builder available
            var doc = System.Xml.Linq.XDocument.Parse(xmlStr);
            return ValueTask.FromResult<object?>(doc);
        }
        catch (XmlException ex)
        {
            throw new XQueryException("FODC0006", $"Error parsing XML: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads XML with safe DTD processing: allows internal DTD subset for entity expansion
    /// but limits entity expansion to prevent billion-laughs attacks.
    /// External entities are resolved via XmlUrlResolver (same as .NET default).
    /// </summary>
    internal static XmlDocument LoadXmlWithDtd(string xmlStr)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            MaxCharactersFromEntities = 1_000_000,
            XmlResolver = new System.Xml.XmlUrlResolver()
        };
        var xmlDoc = new XmlDocument();
        xmlDoc.PreserveWhitespace = true;
        using var reader = XmlReader.Create(new System.IO.StringReader(xmlStr), settings);
        xmlDoc.Load(reader);
        return xmlDoc;
    }

    // ─── ConvertToXdm ──────────────────────────────────────────────────────

    /// <summary>
    /// Converts a System.Xml.XmlDocument to an XDM document tree using an INodeBuilder.
    /// </summary>
    internal static XdmDocument ConvertToXdm(XmlDocument doc, INodeBuilder builder, string? documentUri = null)
    {
        var docId = builder.AllocateId();
        var docElementId = NodeId.None;

        var children = new List<NodeId>();
        foreach (XmlNode child in doc.ChildNodes)
        {
            // Skip text nodes (including whitespace) at the document level.
            // While the XDM spec allows text children of document nodes, in practice
            // XML parsers may add whitespace artifacts. Exclude them from children but
            // still include in string value (computed from doc.InnerText).
            if (child.NodeType is XmlNodeType.Text or XmlNodeType.Whitespace
                or XmlNodeType.SignificantWhitespace or XmlNodeType.CDATA)
                continue;

            var effectiveDocUri = documentUri ?? doc.BaseURI;
            var childNode = ConvertXmlNode(child, builder, docId, new DocumentId(1), effectiveDocUri);
            if (childNode != null)
            {
                children.Add(childNode.Id);
                if (childNode is XdmElement && docElementId == NodeId.None)
                    docElementId = childNode.Id;
            }
        }

        var docNode = new XdmDocument
        {
            Id = docId,
            Document = new DocumentId(1),
            Parent = NodeId.None,
            DocumentElement = docElementId,
            DocumentUri = documentUri ?? doc.BaseURI,
            Children = children,
            DocumentElementLocalName = doc.DocumentElement?.LocalName
        };
        // Compute string value (concatenation of all descendant text nodes).
        // Use DocumentElement.InnerText to exclude document-level whitespace text nodes
        // that aren't modeled as XDM children (we skip text at document level above).
        docNode._stringValue = doc.DocumentElement?.InnerText ?? "";

        builder.RegisterNode(docNode);
        return docNode;
    }

    private static XdmNode? ConvertXmlNode(XmlNode xmlNode, INodeBuilder builder, NodeId parentId, DocumentId docId, string? documentBaseUri = null)
    {
        switch (xmlNode.NodeType)
        {
            case XmlNodeType.Element:
            {
                var elemId = builder.AllocateId();
                var elemNsId = builder.InternNamespace(xmlNode.NamespaceURI ?? "");

                // Collect all in-scope namespace declarations (including inherited ones)
                var nsDecls = new List<NamespaceBinding>();
                var seenPrefixes = new HashSet<string>();
                if (xmlNode is XmlElement xmlElem)
                {
                    var nav = xmlElem.CreateNavigator()!;
                    foreach (var kvp in nav.GetNamespacesInScope(System.Xml.XmlNamespaceScope.All))
                    {
                        if (kvp.Key == "xml")
                            continue; // Skip xml namespace
                        nsDecls.Add(new NamespaceBinding(kvp.Key, builder.InternNamespace(kvp.Value)));
                        seenPrefixes.Add(kvp.Key);
                    }
                }
                else if (xmlNode.Attributes != null)
                {
                    foreach (XmlAttribute attr in xmlNode.Attributes)
                    {
                        if (attr.Name == "xmlns")
                        {
                            nsDecls.Add(new NamespaceBinding("", builder.InternNamespace(attr.Value)));
                        }
                        else if (attr.Name.StartsWith("xmlns:", StringComparison.Ordinal))
                        {
                            var prefix = attr.Name[6..];
                            nsDecls.Add(new NamespaceBinding(prefix, builder.InternNamespace(attr.Value)));
                        }
                    }
                }

                // Convert attributes
                var attrIds = new List<NodeId>();
                if (xmlNode.Attributes != null)
                {
                    foreach (XmlAttribute attr in xmlNode.Attributes)
                    {
                        // Skip xmlns declarations
                        if (attr.Name == "xmlns" || attr.Name.StartsWith("xmlns:", StringComparison.Ordinal))
                            continue;

                        var attrId = builder.AllocateId();
                        var isId = (attr.LocalName == "id" && attr.Prefix == "xml")
                            || attr.SchemaInfo?.SchemaType?.TypeCode == System.Xml.Schema.XmlTypeCode.Id
                            || (attr.OwnerDocument?.GetElementById(attr.Value) == attr.OwnerElement
                                && attr.OwnerElement != null);
                        var xdmAttr = new XdmAttribute
                        {
                            Id = attrId,
                            Document = docId,
                            Parent = elemId,
                            Namespace = builder.InternNamespace(attr.NamespaceURI ?? ""),
                            LocalName = attr.LocalName,
                            Prefix = string.IsNullOrEmpty(attr.Prefix) ? null : attr.Prefix,
                            Value = attr.Value,
                            IsId = isId
                        };
                        builder.RegisterNode(xdmAttr);
                        attrIds.Add(attrId);
                    }
                }

                // Convert children
                var childIds = new List<NodeId>();
                foreach (XmlNode child in xmlNode.ChildNodes)
                {
                    var childNode = ConvertXmlNode(child, builder, elemId, docId, documentBaseUri);
                    if (childNode != null)
                        childIds.Add(childNode.Id);
                }

                // Compute string value (concatenation of all descendant text)
                var stringValue = xmlNode.InnerText;

                // Capture entity-derived base URI when it differs from the document's base URI
                string? entityBaseUri = null;
                if (!string.IsNullOrEmpty(xmlNode.BaseURI) && documentBaseUri != null
                    && !string.Equals(xmlNode.BaseURI, documentBaseUri, StringComparison.Ordinal))
                {
                    entityBaseUri = xmlNode.BaseURI;
                }

                var elem = new XdmElement
                {
                    Id = elemId,
                    Document = docId,
                    Parent = parentId,
                    Namespace = elemNsId,
                    LocalName = xmlNode.LocalName,
                    Prefix = string.IsNullOrEmpty(xmlNode.Prefix) ? null : xmlNode.Prefix,
                    BaseUri = entityBaseUri,
                    Attributes = attrIds,
                    Children = childIds,
                    NamespaceDeclarations = nsDecls.Count > 0
                        ? nsDecls.ToArray()
                        : XdmElement.EmptyNamespaceDeclarations
                };
                elem._stringValue = stringValue;
                builder.RegisterNode(elem);
                return elem;
            }

            case XmlNodeType.Text:
            case XmlNodeType.CDATA:
            case XmlNodeType.Whitespace:
            case XmlNodeType.SignificantWhitespace:
            {
                var textId = builder.AllocateId();
                var text = new XdmText
                {
                    Id = textId,
                    Document = docId,
                    Parent = parentId,
                    Value = xmlNode.Value ?? ""
                };
                builder.RegisterNode(text);
                return text;
            }

            case XmlNodeType.Comment:
            {
                var commentId = builder.AllocateId();
                var comment = new XdmComment
                {
                    Id = commentId,
                    Document = docId,
                    Parent = parentId,
                    Value = xmlNode.Value ?? ""
                };
                builder.RegisterNode(comment);
                return comment;
            }

            case XmlNodeType.ProcessingInstruction:
            {
                var piId = builder.AllocateId();
                // Capture entity-derived base URI for PIs
                string? piBaseUri = null;
                if (!string.IsNullOrEmpty(xmlNode.BaseURI) && documentBaseUri != null
                    && !string.Equals(xmlNode.BaseURI, documentBaseUri, StringComparison.Ordinal))
                {
                    piBaseUri = xmlNode.BaseURI;
                }
                var pi = new XdmProcessingInstruction
                {
                    Id = piId,
                    Document = docId,
                    Parent = parentId,
                    Target = xmlNode.Name,
                    Value = xmlNode.Value ?? "",
                    BaseUri = piBaseUri
                };
                builder.RegisterNode(pi);
                return pi;
            }

            default:
                return null;
        }
    }
}

// ─── fn:parse-xml-fragment ──────────────────────────────────────────────────

/// <summary>
/// fn:parse-xml-fragment($arg as xs:string?) as document-node()?
/// Parses an XML fragment from a string and returns a document node.
/// </summary>
public sealed class ParseXmlFragmentFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-xml-fragment");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Document, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(null);
        var xmlStr = arg.ToString() ?? "";
        if (string.IsNullOrEmpty(xmlStr))
        {
            if (context.NodeStore is INodeBuilder builder)
            {
                // Create an empty XDM document node directly
                var docId = builder.AllocateId();
                var emptyDoc = new XdmDocument
                {
                    Id = docId,
                    Document = new DocumentId(1),
                    Parent = NodeId.None,
                    DocumentElement = NodeId.None,
                    DocumentUri = null,
                    Children = [],
                };
                emptyDoc._stringValue = "";
                builder.RegisterNode(emptyDoc);
                return ValueTask.FromResult<object?>(emptyDoc);
            }
            return ValueTask.FromResult<object?>(new System.Xml.Linq.XDocument());
        }
        try
        {
            if (context.NodeStore is INodeBuilder builder2)
            {
                // Wrap in a root element to handle fragments with multiple roots or text content
                XmlDocument xmlDoc;
                var wasWrapped = false;
                try
                {
                    xmlDoc = ParseXmlFunction.LoadXmlWithDtd(xmlStr);
                }
                catch (XmlException)
                {
                    xmlDoc = ParseXmlFunction.LoadXmlWithDtd($"<_rtf_root_>{xmlStr}</_rtf_root_>");
                    wasWrapped = true;
                }
                var xdmDoc = ParseXmlFunction.ConvertToXdm(xmlDoc, builder2, documentUri: null);
                xdmDoc.DocumentUri = null;
                if (wasWrapped)
                    xdmDoc.BaseUri = null;
                return ValueTask.FromResult<object?>(xdmDoc);
            }
            // Fallback to LINQ XDocument when no node builder available
            var wrapped = $"<_r>{xmlStr}</_r>";
            var tempDoc = System.Xml.Linq.XDocument.Parse(wrapped);
            var doc = new System.Xml.Linq.XDocument();
            foreach (var node in tempDoc.Root!.Nodes())
                doc.Add(node);
            return ValueTask.FromResult<object?>(doc);
        }
        catch (XmlException ex)
        {
            throw new XQueryException("FODC0006", $"Error parsing XML fragment: {ex.Message}");
        }
    }
}
