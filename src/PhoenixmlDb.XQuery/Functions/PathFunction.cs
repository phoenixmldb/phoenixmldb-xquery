using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:path($node as node()?) as xs:string? — returns the path expression for a node.
/// </summary>
public sealed class PathFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "path");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.OptionalNode }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var node = arguments[0] as XdmNode;
        if (node is null)
            return ValueTask.FromResult<object?>(null);

        return ValueTask.FromResult<object?>(ComputePath(node, context.NodeStore));
    }

    /// <summary>
    /// Computes the XPath path expression for a node, following the fn:path specification.
    /// </summary>
    internal static string ComputePath(XdmNode node, INodeStore? store)
    {
        if (node is XdmDocument)
            return "/";

        const string orphanRoot = "Q{http://www.w3.org/2005/xpath-functions}root()";

        // Find the tree root first
        var treeRoot = node;
        while (treeRoot.Parent.HasValue && treeRoot.Parent.Value != NodeId.None && store != null)
        {
            var parent = store.GetNode(treeRoot.Parent.Value);
            if (parent == null)
                break;
            treeRoot = parent;
        }

        bool isDocumentRooted = treeRoot is XdmDocument;

        // If the node IS the root of an orphan tree, return Q{...}root()
        if (!isDocumentRooted && ReferenceEquals(node, treeRoot))
            return orphanRoot;

        // Build path from node up to (but not including) the tree root
        var segments = new List<string>();
        var current = node;

        while (current != null && !ReferenceEquals(current, treeRoot))
        {
            segments.Add(GetSegment(current, store));

            if (current.Parent.HasValue && current.Parent.Value != NodeId.None && store != null)
                current = store.GetNode(current.Parent.Value);
            else
                current = null;
        }

        segments.Reverse();

        if (isDocumentRooted)
            return "/" + string.Join("/", segments);
        else
            return orphanRoot + "/" + string.Join("/", segments);
    }

    private static string GetSegment(XdmNode node, INodeStore? store)
    {
        switch (node)
        {
            case XdmElement elem:
            {
                var nsUri = store?.GetNamespaceUri(elem.Namespace) ?? "";
                var position = GetSiblingPosition(node, store);
                return $"Q{{{nsUri}}}{elem.LocalName}[{position}]";
            }

            case XdmAttribute attr:
            {
                var nsUri = store?.GetNamespaceUri(attr.Namespace) ?? "";
                if (string.IsNullOrEmpty(nsUri))
                    return $"@{attr.LocalName}";
                return $"@Q{{{nsUri}}}{attr.LocalName}";
            }

            case XdmText:
            {
                var position = GetSiblingPosition(node, store, XdmNodeKind.Text);
                return $"text()[{position}]";
            }

            case XdmComment:
            {
                var position = GetSiblingPosition(node, store, XdmNodeKind.Comment);
                return $"comment()[{position}]";
            }

            case XdmProcessingInstruction pi:
            {
                var position = GetSiblingPosition(node, store, XdmNodeKind.ProcessingInstruction, pi.Target);
                return $"processing-instruction({pi.Target})[{position}]";
            }

            case XdmNamespace ns:
            {
                if (string.IsNullOrEmpty(ns.Prefix))
                    return "namespace::*[Q{http://www.w3.org/2005/xpath-functions}local-name()=\"\"]";
                return $"namespace::{ns.Prefix}";
            }

            default:
                return "unknown()";
        }
    }

    /// <summary>
    /// Gets the 1-based position of this node among its like-named/like-typed siblings.
    /// </summary>
    private static int GetSiblingPosition(XdmNode node, INodeStore? store, XdmNodeKind? filterKind = null, string? filterName = null)
    {
        if (store == null || !node.Parent.HasValue)
            return 1;

        var parent = store.GetNode(node.Parent.Value);
        if (parent == null)
            return 1;

        var childIds = parent switch
        {
            XdmDocument doc => doc.Children,
            XdmElement elem => elem.Children,
            _ => (IReadOnlyList<NodeId>)[]
        };

        int position = 0;
        foreach (var childId in childIds)
        {
            var child = store.GetNode(childId);
            if (child == null)
                continue;

            bool matches;
            if (node is XdmElement targetElem)
            {
                // For elements, match by expanded name
                matches = child is XdmElement ce
                    && ce.LocalName == targetElem.LocalName
                    && ce.Namespace == targetElem.Namespace;
            }
            else if (filterKind.HasValue)
            {
                matches = child.NodeKind == filterKind.Value;
                if (matches && filterName != null && child is XdmProcessingInstruction pi)
                    matches = pi.Target == filterName;
            }
            else
            {
                matches = child.NodeKind == node.NodeKind;
            }

            if (matches)
            {
                position++;
                if (child.Id == node.Id)
                    return position;
            }
        }

        return 1; // fallback
    }
}
