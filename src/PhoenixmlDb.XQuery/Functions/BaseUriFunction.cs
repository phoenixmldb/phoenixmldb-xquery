using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:base-uri($arg) as xs:anyURI?
/// </summary>
public sealed class BaseUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "base-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is not XdmNode node)
            return ValueTask.FromResult<object?>(null);

        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var uri = ComputeBaseUri(node, nodeProvider);
        return ValueTask.FromResult<object?>(uri != null ? new XsAnyUri(uri) : null);
    }

    /// <summary>
    /// Computes the base URI for a node by walking up the ancestor chain.
    /// Per XPath spec: element nodes inherit from xml:base or parent; document nodes use document-uri.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1055")]
    public static string? ComputeBaseUri(XdmNode node, INodeProvider? nodeProvider)
    {
        if (node is XdmDocument doc)
            return doc.BaseUri;

        // For attribute nodes, the base URI is the base URI of the parent element
        if (node is XdmAttribute && nodeProvider != null && node.Parent.HasValue && node.Parent.Value != NodeId.None)
        {
            var parentNode = nodeProvider.GetNode(node.Parent.Value);
            if (parentNode != null)
                return ComputeBaseUri(parentNode, nodeProvider);
        }

        // For element nodes, check xml:base first, then walk up
        if (node is XdmElement elem && nodeProvider != null)
        {
            string? xmlBase = null;
            foreach (var attrId in elem.Attributes)
            {
                var attr = nodeProvider.GetNode(attrId) as XdmAttribute;
                if (attr != null && attr.LocalName == "base"
                    && (attr.Namespace == NamespaceId.Xml || attr.Prefix == "xml"))
                {
                    xmlBase = attr.Value;
                    break;
                }
            }

            if (xmlBase != null)
            {
                // XQST0046/FORG0001: reject malformed percent-escapes in xml:base
                for (int i = 0; i < xmlBase.Length; i++)
                {
                    if (xmlBase[i] == '%')
                    {
                        if (i + 2 >= xmlBase.Length || !Uri.IsHexDigit(xmlBase[i + 1]) || !Uri.IsHexDigit(xmlBase[i + 2]))
                            throw new XQueryRuntimeException("FORG0001",
                                $"Invalid xml:base URI: '{xmlBase}'");
                        i += 2;
                    }
                }

                var parentBaseUri = GetParentBaseUri(elem, nodeProvider);
                // For parentless nodes (e.g., copy-of into a variable), fall back to the
                // element's own BaseUri property (the construction context base URI)
                if (parentBaseUri == null && elem.BaseUri != null)
                    parentBaseUri = elem.BaseUri;
                if (parentBaseUri != null && Uri.TryCreate(parentBaseUri, UriKind.Absolute, out var parentUri)
                    && Uri.TryCreate(parentUri, xmlBase, out var resolved))
                    return resolved.OriginalString;
                return xmlBase;
            }

            // Check for entity-derived base URI (set during DTD entity expansion)
            if (elem.BaseUri != null)
                return elem.BaseUri;

            // XDM dm:base-uri: a copied element with NO xml:base attribute (and no
            // entity-derived BaseUri — both handled above) that has a PARENT inherits
            // the PARENT's base URI. The preserved SOURCE base URI (CopySourceBaseUri,
            // recorded when a shallow copy dropped the source's base) applies ONLY when
            // the copy is PARENTLESS. Hence check the parent FIRST; use the source base
            // as the parentless fallback. (fn/base-uri 025,033,035,040: a shallow copy
            // placed under a new parent must report the new parent's base, not the
            // source's; parentless copies 024/026/027 are unaffected because their
            // parent base is null and the source base remains the answer.)
            var parentBase = GetParentBaseUri(elem, nodeProvider);
            if (parentBase != null)
                return parentBase;

            if (elem.CopySourceBaseUri != null)
                return elem.CopySourceBaseUri;

            return null;
        }

        // For text, comment, PI — check entity-derived base URI first, then inherit from parent
        if (node.BaseUri != null)
            return node.BaseUri;

        if (nodeProvider != null && node.Parent.HasValue && node.Parent.Value != NodeId.None)
        {
            var parentNode = nodeProvider.GetNode(node.Parent.Value);
            if (parentNode != null)
                return ComputeBaseUri(parentNode, nodeProvider);
        }

        return node.BaseUri;
    }

    private static string? GetParentBaseUri(XdmNode node, INodeProvider nodeProvider)
    {
        if (!node.Parent.HasValue || node.Parent.Value == NodeId.None)
            return null;
        var parentNode = nodeProvider.GetNode(node.Parent.Value);
        return parentNode != null ? ComputeBaseUri(parentNode, nodeProvider) : null;
    }
}
