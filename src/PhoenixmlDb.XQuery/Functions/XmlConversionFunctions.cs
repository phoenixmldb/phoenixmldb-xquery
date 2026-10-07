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
                var xmlDoc = LoadXmlWithDtd(xmlStr, context.StaticBaseUri, context.ResourcePolicy);
                var xdmDoc = ConvertToXdm(xmlDoc, builder, documentUri: null,
                    (context as Execution.QueryExecutionContext)?.CancellationToken ?? default);
                // Document URI is absent per F&O §14.9.1, but base URI = static-base-uri
                xdmDoc.DocumentUri = null;
                xdmDoc.BaseUri = context.StaticBaseUri;
                return ValueTask.FromResult<object?>(xdmDoc);
            }
            // Fallback to LINQ XDocument when no node builder available
            var doc = System.Xml.Linq.XDocument.Parse(xmlStr);
            return ValueTask.FromResult<object?>(doc);
        }
        catch (XmlException ex)
        {
            throw context.Error("FODC0006", $"Error parsing XML: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads XML with safe DTD processing: allows internal DTD subset for entity expansion
    /// but limits entity expansion to prevent billion-laughs attacks.
    /// </summary>
    /// <remarks>
    /// External entities and external DTD subsets: with no resource policy they are resolved
    /// via XmlUrlResolver (the .NET default). Under a policy they are fetched only when it
    /// allows DTD processing, and then only from locations it allows (<see
    /// cref="Security.PolicyXmlResolver"/>); otherwise nothing external is read at all.
    /// </remarks>
    internal static XmlDocument LoadXmlWithDtd(string xmlStr, string? baseUri = null, Security.ResourcePolicy? policy = null)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            MaxCharactersFromEntities = 1_000_000,
            XmlResolver = policy is null ? new System.Xml.XmlUrlResolver()
                : policy.AllowDtdProcessing ? new Security.PolicyXmlResolver(policy)
                : null,
        };
        var xmlDoc = new XmlDocument();
        xmlDoc.PreserveWhitespace = true;
        using var reader = baseUri != null
            ? XmlReader.Create(new System.IO.StringReader(xmlStr), settings, baseUri)
            : XmlReader.Create(new System.IO.StringReader(xmlStr), settings);
        xmlDoc.Load(reader);
        return xmlDoc;
    }

    // ─── ConvertToXdm ──────────────────────────────────────────────────────

    /// <summary>
    /// Converts a System.Xml.XmlDocument to an XDM document tree using an INodeBuilder.
    /// </summary>
    internal static XdmDocument ConvertToXdm(XmlDocument doc, INodeBuilder builder, string? documentUri = null,
        CancellationToken cancellationToken = default)
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
            var childNode = ConvertXmlNode(child, builder, docId, new DocumentId(1), effectiveDocUri,
                cancellationToken: cancellationToken);
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

    /// <summary>
    /// Converts an element and its whole subtree without recursion (#102): an explicit stack of
    /// open elements, children converted in document order, each element built once its children
    /// are. Recursing once per nesting level overflowed the stack — an uncatchable crash that took
    /// the process down — on a 20,000-deep document.
    /// </summary>
    private static XdmNode ConvertElementTree(XmlElement root, INodeBuilder builder, NodeId rootParentId, DocumentId docId, string? documentBaseUri,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<ElementFrame>();
        stack.Push(BeginElement(root, builder, rootParentId, docId, parentScope: null, inheritedBaseUri: null));
        var steps = 0;
        while (true)
        {
            // A large document is a long loop with nothing else in it to notice a cancellation.
            if ((++steps & 0xFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var frame = stack.Peek();
            if (frame.Children.MoveNext())
            {
                var child = (XmlNode)frame.Children.Current!;
                if (child is XmlElement childElem)
                {
                    stack.Push(BeginElement(childElem, builder, frame.Id, docId, frame.NamespaceDeclarations, frame.BaseUri));
                }
                else
                {
                    var converted = ConvertXmlNode(child, builder, frame.Id, docId, documentBaseUri,
                        nodeBaseUri: child.ParentNode == frame.Node ? frame.BaseUri : null,
                        cancellationToken: cancellationToken);
                    if (converted != null)
                        frame.ChildIds.Add(converted.Id);
                    if (child.NodeType is XmlNodeType.Text or XmlNodeType.CDATA
                        or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                        frame.Text.Append(child.Value);
                }
                continue;
            }
            stack.Pop();
            var elem = FinishElement(frame, docId, documentBaseUri);
            builder.RegisterNode(elem);
            if (stack.Count == 0)
                return elem;
            var parent = stack.Peek();
            parent.ChildIds.Add(elem.Id);
            parent.Text.Append(frame.Text);
        }
    }

    private sealed class ElementFrame
    {
        public required XmlElement Node { get; init; }
        public required NodeId Id { get; init; }
        public required NodeId ParentId { get; init; }
        public required PhoenixmlDb.Core.NamespaceId NamespaceId { get; init; }
        public required List<NamespaceBinding> NamespaceDeclarations { get; init; }
        public required List<NodeId> AttributeIds { get; init; }
        public required System.Collections.IEnumerator Children { get; init; }
        // XmlNode.BaseURI of this element. A child element or processing instruction has the same
        // one, so it is handed down. Asking each node instead walks every ancestor, which made
        // conversion quadratic in depth again: 64,000 nested elements took 6 s, 128,000 took 25 s.
        public required string BaseUri { get; init; }
        public List<NodeId> ChildIds { get; } = [];
        // The element's string value, built from its children as they are converted. XmlNode.InnerText
        // computes the same thing by recursing once per level, which overflows a 1 MB (Windows) stack
        // on a deep document even when conversion itself no longer recurses.
        public System.Text.StringBuilder Text { get; } = new();
    }

    private static ElementFrame BeginElement(XmlElement xmlElem2, INodeBuilder builder, NodeId parentId, DocumentId docId,
        List<NamespaceBinding>? parentScope, string? inheritedBaseUri)
    {
                var elemId = builder.AllocateId();
                var elemNsId = builder.InternNamespace(xmlElem2.NamespaceURI ?? "");

                // Collect all in-scope namespace declarations (including inherited ones)
                var nsDecls = new List<NamespaceBinding>();
                var seenPrefixes = new HashSet<string>();
                // An element that declares no namespace of its own has exactly its parent's in-scope
                // namespaces, in the same order, so it shares the parent's list. Only a declaring
                // element pays for GetNamespacesInScope, which walks every ancestor (#102: that made
                // conversion O(depth²)).
                var declaresOwn = false;
                foreach (XmlAttribute a in xmlElem2.Attributes)
                    if (a.Name == "xmlns" || a.Name.StartsWith("xmlns:", StringComparison.Ordinal)) { declaresOwn = true; break; }
                if (parentScope != null && !declaresOwn)
                {
                    nsDecls = parentScope;
                }
                else if (xmlElem2 is XmlElement xmlElem)
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
                else if (xmlElem2.Attributes != null)
                {
                    foreach (XmlAttribute attr in xmlElem2.Attributes)
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
                if (xmlElem2.Attributes != null)
                {
                    foreach (XmlAttribute attr in xmlElem2.Attributes)
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

        return new ElementFrame
        {
            Node = xmlElem2,
            Id = elemId,
            ParentId = parentId,
            NamespaceId = elemNsId,
            NamespaceDeclarations = nsDecls,
            AttributeIds = attrIds,
            Children = xmlElem2.ChildNodes.GetEnumerator(),
            BaseUri = inheritedBaseUri ?? xmlElem2.BaseURI,
        };
    }

    private static XdmElement FinishElement(ElementFrame frame, DocumentId docId, string? documentBaseUri)
    {
        var xmlNode = frame.Node;
        var elemId = frame.Id;
        var parentId = frame.ParentId;
        var elemNsId = frame.NamespaceId;
        var nsDecls = frame.NamespaceDeclarations;
        var attrIds = frame.AttributeIds;
        var childIds = frame.ChildIds;
                // Compute string value (concatenation of all descendant text)
                var stringValue = frame.Text.ToString();

                // Capture entity-derived base URI when it differs from the document's base URI
                string? entityBaseUri = null;
                if (!string.IsNullOrEmpty(frame.BaseUri) && documentBaseUri != null
                    && !string.Equals(frame.BaseUri, documentBaseUri, StringComparison.Ordinal))
                {
                    entityBaseUri = frame.BaseUri;
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
        return elem;
    }

    private static XdmNode? ConvertXmlNode(XmlNode xmlNode, INodeBuilder builder, NodeId parentId, DocumentId docId, string? documentBaseUri = null,
        string? nodeBaseUri = null, CancellationToken cancellationToken = default)
    {
        switch (xmlNode.NodeType)
        {
            case XmlNodeType.Element:
                return ConvertElementTree((XmlElement)xmlNode, builder, parentId, docId, documentBaseUri, cancellationToken);

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
                var ownBaseUri = documentBaseUri is null ? null : nodeBaseUri ?? xmlNode.BaseURI;
                if (!string.IsNullOrEmpty(ownBaseUri)
                    && !string.Equals(ownBaseUri, documentBaseUri, StringComparison.Ordinal))
                {
                    piBaseUri = ownBaseUri;
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
        // Validate text declaration and DOCTYPE constraints per XPath F&O §14.9.2:
        // - A text declaration (<?xml ...?>) MUST contain encoding; standalone is disallowed
        // - DOCTYPE declarations are not allowed in external parsed entities
        ValidateFragmentConstraints(xmlStr);
        try
        {
            if (context.NodeStore is INodeBuilder builder2)
            {
                // Wrap in a root element to handle fragments with multiple roots or text content
                XmlDocument xmlDoc;
                var wasWrapped = false;
                try
                {
                    xmlDoc = ParseXmlFunction.LoadXmlWithDtd(xmlStr, policy: context.ResourcePolicy);
                }
                catch (XmlException)
                {
                    // Strip XML/text declaration before wrapping — it can't be inside an element
                    var fragStr = xmlStr;
                    if (fragStr.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                    {
                        var endDecl = fragStr.IndexOf("?>", StringComparison.Ordinal);
                        if (endDecl >= 0)
                            fragStr = fragStr[(endDecl + 2)..];
                    }
                    xmlDoc = ParseXmlFunction.LoadXmlWithDtd($"<_rtf_root_>{fragStr}</_rtf_root_>", policy: context.ResourcePolicy);
                    wasWrapped = true;
                }
                if (!wasWrapped)
                {
                    var xdmDoc = ParseXmlFunction.ConvertToXdm(xmlDoc, builder2, documentUri: null);
                    xdmDoc.DocumentUri = null;
                    return ValueTask.FromResult<object?>(xdmDoc);
                }
                else
                {
                    // Build an unwrapped document: the wrapper element's children become
                    // direct children of the document node.
                    var wrappedDoc = ParseXmlFunction.ConvertToXdm(xmlDoc, builder2, documentUri: null);
                    var wrapperElem = wrappedDoc.DocumentElement.HasValue
                        ? builder2.GetNode(wrappedDoc.DocumentElement.Value) as XdmElement : null;
                    if (wrapperElem == null || wrapperElem.LocalName != "_rtf_root_")
                    {
                        wrappedDoc.DocumentUri = null;
                        return ValueTask.FromResult<object?>(wrappedDoc);
                    }

                    // Create a new document node with the wrapper's children
                    var newDocId = builder2.AllocateId();
                    NodeId? newDocElem = null;
                    foreach (var childId in wrapperElem.Children)
                    {
                        var child = builder2.GetNode(childId);
                        if (child != null)
                            child.Parent = newDocId;
                        if (child is XdmElement && newDocElem == null)
                            newDocElem = childId;
                    }

                    var sv = new System.Text.StringBuilder();
                    foreach (var childId in wrapperElem.Children)
                    {
                        var child = builder2.GetNode(childId);
                        if (child is XdmText txt) sv.Append(txt.Value);
                        else if (child is XdmElement elem) sv.Append(elem.StringValue);
                    }

                    var newDoc = new XdmDocument
                    {
                        Id = newDocId,
                        Document = new DocumentId(1),
                        Parent = NodeId.None,
                        DocumentElement = newDocElem,
                        DocumentUri = null,
                        Children = wrapperElem.Children,
                    };
                    newDoc._stringValue = sv.ToString();
                    builder2.RegisterNode(newDoc);
                    return ValueTask.FromResult<object?>(newDoc);
                }
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
            throw context.Error("FODC0006", $"Error parsing XML fragment: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates parse-xml-fragment constraints from XPath F&amp;O 14.9.2:
    /// - Text declarations must have encoding; standalone is disallowed
    /// - DOCTYPE declarations are not allowed
    /// </summary>
    private static void ValidateFragmentConstraints(string xmlStr, Ast.ExecutionContext? context = null)
    {
        var trimmed = xmlStr.TrimStart();

        // Check for DOCTYPE — not allowed in external parsed entities
        if (trimmed.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
            throw context.Error("FODC0006",
                "DOCTYPE declarations are not allowed in parse-xml-fragment input");

        // Check text declaration constraints (<?xml ...?>)
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            && trimmed.Length > 5 && (char.IsWhiteSpace(trimmed[5]) || trimmed[5] == '?'))
        {
            var endDecl = trimmed.IndexOf("?>", StringComparison.Ordinal);
            if (endDecl >= 0)
            {
                var decl = trimmed[..endDecl];
                // Text declaration must contain encoding
                if (!decl.Contains("encoding", StringComparison.OrdinalIgnoreCase))
                    throw context.Error("FODC0006",
                        "Text declaration in parse-xml-fragment must contain an encoding declaration");
                // Text declaration must not contain standalone
                if (decl.Contains("standalone", StringComparison.OrdinalIgnoreCase))
                    throw context.Error("FODC0006",
                        "Text declaration in parse-xml-fragment must not contain a standalone declaration");
            }
        }
    }
}
