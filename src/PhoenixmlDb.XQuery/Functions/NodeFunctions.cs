using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:name($arg) as xs:string
/// </summary>
public sealed class NameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "name");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0] is object[] arr ? (arr.Length > 0 ? arr[0] : null) : arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>("");

        return arg switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(
                !string.IsNullOrEmpty(elem.Prefix) ? $"{elem.Prefix}:{elem.LocalName}" : elem.LocalName),
            XdmAttribute attr => ValueTask.FromResult<object?>(
                !string.IsNullOrEmpty(attr.Prefix) ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName),
            XdmProcessingInstruction pi => ValueTask.FromResult<object?>(pi.Target),
            XdmNamespace ns => ValueTask.FromResult<object?>(ns.Prefix),
            _ => ValueTask.FromResult<object?>("")
        };
    }
}

/// <summary>
/// fn:name() as xs:string (uses context item)
/// </summary>
public sealed class Name0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "name");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Use context item
        var item = context.ContextItem;
        if (item == null)
            throw new XQueryRuntimeException("XPDY0002", "Context item is absent in fn:name()");

        return item switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(
                !string.IsNullOrEmpty(elem.Prefix) ? $"{elem.Prefix}:{elem.LocalName}" : elem.LocalName),
            XdmAttribute attr => ValueTask.FromResult<object?>(
                !string.IsNullOrEmpty(attr.Prefix) ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName),
            XdmProcessingInstruction pi => ValueTask.FromResult<object?>(pi.Target),
            XdmNamespace ns => ValueTask.FromResult<object?>(ns.Prefix),
            XdmDocument or XdmText or XdmComment or TextNodeItem => ValueTask.FromResult<object?>(""),
            _ => throw new XQueryRuntimeException("XPTY0004", "Context item is not a node in fn:name()")
        };
    }
}

/// <summary>
/// fn:local-name($arg) as xs:string
/// </summary>
public sealed class LocalNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "local-name");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0] is object[] arr ? (arr.Length > 0 ? arr[0] : null) : arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>("");

        return arg switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(elem.LocalName),
            XdmAttribute attr => ValueTask.FromResult<object?>(attr.LocalName),
            XdmProcessingInstruction pi => ValueTask.FromResult<object?>(pi.Target),
            XdmNamespace ns => ValueTask.FromResult<object?>(ns.Prefix),
            _ => ValueTask.FromResult<object?>("")
        };
    }
}

/// <summary>
/// fn:local-name() as xs:string (uses context item)
/// </summary>
public sealed class LocalName0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "local-name");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Use context item
        var item = context.ContextItem;
        if (item == null)
            throw new XQueryRuntimeException("XPDY0002", "Context item is absent in fn:local-name()");

        return item switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(elem.LocalName),
            XdmAttribute attr => ValueTask.FromResult<object?>(attr.LocalName),
            XdmProcessingInstruction pi => ValueTask.FromResult<object?>(pi.Target),
            XdmNamespace ns => ValueTask.FromResult<object?>(ns.Prefix),
            XdmDocument or XdmText or XdmComment or TextNodeItem => ValueTask.FromResult<object?>(""),
            _ => throw new XQueryRuntimeException("XPTY0004", "Context item is not a node in fn:local-name()")
        };
    }
}

/// <summary>
/// fn:namespace-uri($arg) as xs:anyURI
/// </summary>
public sealed class NamespaceUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "namespace-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0] is object[] arr ? (arr.Length > 0 ? arr[0] : null) : arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>("");

        var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
        return arg switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(ResolveNsId(elem.Namespace, qec)),
            XdmAttribute attr => ValueTask.FromResult<object?>(ResolveNsId(attr.Namespace, qec)),
            XdmNamespace ns => ValueTask.FromResult<object?>(ns.Uri),
            _ => ValueTask.FromResult<object?>("")
        };
    }

    internal static string ResolveNsId(NamespaceId id, Execution.QueryExecutionContext? qec)
    {
        if (id == NamespaceId.None) return "";
        // Try explicit namespace resolver first
        if (qec?.NamespaceResolver != null)
        {
            var result = qec.NamespaceResolver(id);
            if (result != null) return result;
        }
        // Fall back to the node provider's namespace resolution (XdmDocumentStore)
        if (qec?.NodeProvider is XdmDocumentStore store)
        {
            var result = store.ResolveNamespaceUri(id);
            if (result != null) return result.ToString();
        }
        return "";
    }
}

/// <summary>
/// fn:namespace-uri() as xs:anyURI (uses context item)
/// </summary>
public sealed class NamespaceUri0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "namespace-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var item = context.ContextItem;
        if (item == null)
            throw new XQueryRuntimeException("XPDY0002", "Context item is absent in fn:namespace-uri()");

        var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
        return item switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(NamespaceUriFunction.ResolveNsId(elem.Namespace, qec)),
            XdmAttribute attr => ValueTask.FromResult<object?>(NamespaceUriFunction.ResolveNsId(attr.Namespace, qec)),
            XdmNamespace ns => ValueTask.FromResult<object?>(ns.Uri),
            XdmDocument or XdmText or XdmComment or XdmProcessingInstruction or TextNodeItem => ValueTask.FromResult<object?>(""),
            _ => throw new XQueryRuntimeException("XPTY0004", "Context item is not a node in fn:namespace-uri()")
        };
    }
}

/// <summary>
/// fn:root($arg) as node()?
/// </summary>
public sealed class RootFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "root");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalNode;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(null);
        if (arg is not XdmNode node)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:root expects a node, got {arg.GetType().Name}");

        return ValueTask.FromResult<object?>(TraverseToRoot(node, context));
    }

    internal static XdmNode TraverseToRoot(XdmNode node, Ast.ExecutionContext context)
    {
        var current = node;
        if (context is PhoenixmlDb.XQuery.Execution.QueryExecutionContext qec && qec.NodeProvider != null)
        {
            while (current.Parent is { } parentId && parentId != NodeId.None)
            {
                var parent = qec.NodeProvider.GetNode(parentId);
                if (parent == null) break;
                current = parent;
            }
        }
        return current;
    }
}

/// <summary>
/// fn:root() as node() (uses context item)
/// </summary>
public sealed class Root0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "root");
    public override XdmSequenceType ReturnType => XdmSequenceType.Node;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext;
        var contextItem = ctx?.ContextItem;
        if (contextItem == null)
            throw new XQueryException("XPDY0002", "Context item is absent");
        if (contextItem is not XdmNode node)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:root expects a node as context item, got {contextItem.GetType().Name}");
        return ValueTask.FromResult<object?>(RootFunction.TraverseToRoot(node, context));
    }
}

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

            var parentBase = GetParentBaseUri(elem, nodeProvider);
            if (parentBase != null)
                return parentBase;

            // Fallback: xsl:copy source base URI (XSLT 3.0 §11.9.1 — copy preserves
            // source base URI, but only as fallback for orphaned nodes without parent)
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

/// <summary>
/// fn:base-uri() as xs:anyURI? (uses context item)
/// </summary>
public sealed class BaseUri0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "base-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is not Execution.QueryExecutionContext qec)
            return ValueTask.FromResult<object?>(null);
        var item = qec.ContextItem;
        if (item is not XdmNode node)
            return ValueTask.FromResult<object?>(null);
        var uri = BaseUriFunction.ComputeBaseUri(node, qec.NodeProvider);
        return ValueTask.FromResult<object?>(uri != null ? new XsAnyUri(uri) : null);
    }
}

/// <summary>
/// fn:static-base-uri() as xs:anyURI?
/// </summary>
public sealed class StaticBaseUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "static-base-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = context.StaticBaseUri;
        return ValueTask.FromResult<object?>(uri != null ? new XsAnyUri(uri) : null);
    }
}

/// <summary>
/// fn:document-uri($arg) as xs:anyURI?
/// </summary>
public sealed class DocumentUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "document-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is XdmDocument doc)
        {
            return ValueTask.FromResult<object?>(doc.DocumentUri);
        }

        return ValueTask.FromResult<object?>(null);
    }
}

/// <summary>
/// fn:node-name($arg) as xs:QName?
/// </summary>
public sealed class NodeNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "node-name");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg != null && arg is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:node-name expects a node, got {arg.GetType().Name}");
        return ValueTask.FromResult<object?>(NodeNameOf(arg, context));
    }

    internal static QName? NodeNameOf(object? node, Ast.ExecutionContext context)
    {
        var resolver = (context as Execution.QueryExecutionContext)?.NamespaceResolver;
        return node switch
        {
            XdmElement elem => new QName(elem.Namespace, elem.LocalName, elem.Prefix)
                { RuntimeNamespace = resolver?.Invoke(elem.Namespace) ?? "" },
            XdmAttribute attr => new QName(attr.Namespace, attr.LocalName, attr.Prefix)
                { RuntimeNamespace = resolver?.Invoke(attr.Namespace) ?? "" },
            XdmProcessingInstruction pi => new QName(NamespaceId.None, pi.Target)
                { RuntimeNamespace = "" },
            XdmNamespace ns => !string.IsNullOrEmpty(ns.Prefix)
                ? new QName(NamespaceId.None, ns.Prefix) { RuntimeNamespace = "" }
                : null, // default namespace node has no name
            _ => null
        };
    }
}

/// <summary>
/// fn:node-name() as xs:QName? — 0-arg version uses context item
/// </summary>
public sealed class NodeName0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "node-name");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext;
        var contextItem = ctx?.ContextItem;
        if (contextItem == null)
            throw new XQueryException("XPDY0002", "Context item is absent");
        if (contextItem is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:node-name expects a node as context item, got {contextItem.GetType().Name}");
        return ValueTask.FromResult<object?>(NodeNameFunction.NodeNameOf(contextItem, context));
    }
}

/// <summary>
/// fn:namespace-uri-for-prefix($prefix, $element) as xs:anyURI?
/// </summary>
public sealed class NamespaceUriForPrefixFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "namespace-uri-for-prefix");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "prefix"), Type = XdmSequenceType.OptionalString },
            new() { Name = new QName(NamespaceId.None, "element"), Type = XdmSequenceType.OptionalNode }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var prefix = arguments[0]?.ToString() ?? "";
        var element = arguments[1] as XdmElement;
        if (element == null) return ValueTask.FromResult<object?>(null);

        // The 'xml' prefix is always implicitly bound
        if (prefix == "xml")
            return ValueTask.FromResult<object?>(new Xdm.XsAnyUri("http://www.w3.org/XML/1998/namespace"));

        var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
        foreach (var ns in element.NamespaceDeclarations)
        {
            if (ns.Prefix == prefix || (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(ns.Prefix)))
                return ValueTask.FromResult<object?>(NamespaceUriFunction.ResolveNsId(ns.Namespace, qec));
        }
        if (element.Prefix == prefix)
            return ValueTask.FromResult<object?>(NamespaceUriFunction.ResolveNsId(element.Namespace, qec));
        return ValueTask.FromResult<object?>(null);
    }
}

/// <summary>
/// fn:namespace-uri-from-QName($arg) as xs:anyURI?
/// </summary>
public sealed class NamespaceUriFromQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "namespace-uri-from-QName");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(null);
        if (arg is QName qn)
        {
            var nsUri = qn.ResolvedNamespace;
            if (nsUri != null)
                return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(nsUri));
            if (qn.Namespace == NamespaceId.None)
                return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(""));
            // Try to resolve via namespace resolver
            var resolver = (context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext)?.NamespaceResolver;
            if (resolver != null)
            {
                var uri = resolver(qn.Namespace);
                if (uri != null)
                    return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(uri));
            }
            return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(qn.Namespace.ToString()));
        }
        // If it's a string QName like "prefix:local", extract namespace from context
        return ValueTask.FromResult<object?>(null);
    }
}

/// <summary>
/// fn:local-name-from-QName($arg) as xs:NCName?
/// </summary>
public sealed class LocalNameFromQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "local-name-from-QName");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(null);
        if (arg is QName qn) return ValueTask.FromResult<object?>(qn.LocalName);
        // If it's a string representation like "prefix:local"
        var str = arg.ToString() ?? "";
        var colonIdx = str.IndexOf(':', StringComparison.Ordinal);
        return ValueTask.FromResult<object?>(colonIdx >= 0 ? str[(colonIdx + 1)..] : str);
    }
}

/// <summary>
/// fn:prefix-from-QName($arg) as xs:NCName?
/// </summary>
public sealed class PrefixFromQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "prefix-from-QName");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(null);
        if (arg is QName qn) return ValueTask.FromResult<object?>(qn.Prefix ?? (object?)null);
        var str = arg.ToString() ?? "";
        var colonIdx = str.IndexOf(':', StringComparison.Ordinal);
        return ValueTask.FromResult<object?>(colonIdx >= 0 ? str[..colonIdx] : null);
    }
}

/// <summary>
/// fn:resolve-QName($qname, $element) as xs:QName?
/// Resolves a QName string using the in-scope namespaces of an element.
/// </summary>
public sealed class ResolveQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "resolve-QName");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "qname"), Type = XdmSequenceType.OptionalString },
            new() { Name = new QName(NamespaceId.None, "element"), Type = new XdmSequenceType { ItemType = ItemType.Element, Occurrence = Occurrence.ExactlyOne } }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var qnameArg = arguments[0];
        if (qnameArg == null) return ValueTask.FromResult<object?>(null);

        var qnameStr = (qnameArg.ToString() ?? "").Trim();
        var elementArg = arguments[1];

        // Split the QName into prefix and local parts
        var colonIdx = qnameStr.IndexOf(':', StringComparison.Ordinal);
        string? prefix;
        string localName;
        if (colonIdx >= 0)
        {
            prefix = qnameStr[..colonIdx];
            localName = qnameStr[(colonIdx + 1)..];
            // Validate: no second colon allowed
            if (localName.Contains(':', StringComparison.Ordinal))
                throw new InvalidOperationException($"FOCA0002: '{qnameStr}' is not a valid QName");
        }
        else
        {
            prefix = null;
            localName = qnameStr;
        }

        // Validate NCName parts
        if ((prefix != null && !IsValidNCName(prefix)) || !IsValidNCName(localName))
            throw new InvalidOperationException($"FOCA0002: '{qnameStr}' is not a valid QName");

        // Handle XdmElement (from RTFs and XDM node store)
        if (elementArg is XdmElement xdmElem)
        {
            Func<NamespaceId, string?>? nsResolver = null;
            if (context is Execution.QueryExecutionContext qec)
                nsResolver = qec.NamespaceResolver;

            string namespaceUri;
            if (prefix == "xml")
            {
                namespaceUri = "http://www.w3.org/XML/1998/namespace";
            }
            else if (prefix != null)
            {
                // Search in-scope namespace declarations (element + ancestors) for the prefix
                string? found = null;
                var nodeStore = context.NodeStore;
                XdmNode? current = xdmElem;
                while (current != null && found == null)
                {
                    if (current is XdmElement elem2)
                    {
                        foreach (var nsDecl in elem2.NamespaceDeclarations)
                        {
                            if (nsDecl.Prefix == prefix)
                            {
                                found = nsResolver?.Invoke(nsDecl.Namespace) ?? "";
                                break;
                            }
                        }
                    }
                    current = current.Parent.HasValue && nodeStore != null
                        ? nodeStore.GetNode(current.Parent.Value) as XdmNode : null;
                }
                if (found == null)
                    throw new Execution.XQueryRuntimeException("FONS0004", $"Prefix '{prefix}' is not declared in the in-scope namespaces");
                namespaceUri = found;
            }
            else
            {
                // No prefix: use the default namespace (element + ancestors)
                namespaceUri = "";
                var nodeStore2 = context.NodeStore;
                XdmNode? current2 = xdmElem;
                while (current2 != null)
                {
                    if (current2 is XdmElement elem3)
                    {
                        foreach (var nsDecl in elem3.NamespaceDeclarations)
                        {
                            if (string.IsNullOrEmpty(nsDecl.Prefix))
                            {
                                namespaceUri = nsResolver?.Invoke(nsDecl.Namespace) ?? "";
                                goto foundDefault;
                            }
                        }
                    }
                    current2 = current2.Parent.HasValue && nodeStore2 != null
                        ? nodeStore2.GetNode(current2.Parent.Value) as XdmNode : null;
                }
                foundDefault:;
            }

            return ValueTask.FromResult<object?>(new QName(NamespaceId.None, localName, prefix) { ExpandedNamespace = namespaceUri });
        }

        // Handle System.Xml.Linq.XElement (from source documents)
        if (elementArg is not System.Xml.Linq.XElement element)
        {
            if (elementArg is System.Xml.Linq.XObject xobj && xobj.Parent is System.Xml.Linq.XElement parentEl)
                element = parentEl;
            else
                throw new InvalidOperationException("FORG0006: Second argument to resolve-QName must be an element");
        }

        // Resolve the namespace using the element's in-scope namespaces
        string nsUri;
        if (prefix != null)
        {
            var ns = element.GetNamespaceOfPrefix(prefix);
            if (ns == null)
                throw new InvalidOperationException($"FONS0004: Prefix '{prefix}' is not declared in the in-scope namespaces");
            nsUri = ns.NamespaceName;
        }
        else
        {
            // No prefix: use the default namespace
            nsUri = element.GetDefaultNamespace().NamespaceName;
        }

        return ValueTask.FromResult<object?>(new QName(NamespaceId.None, localName, prefix) { ExpandedNamespace = nsUri });
    }

    private static bool IsValidNCName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var first = name[0];
        if (first != '_' && !char.IsLetter(first)) return false;
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_' && !char.IsControl(c)
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.SpacingCombiningMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.EnclosingMark)
                return false;
        }
        return true;
    }
}

/// <summary>
/// fn:in-scope-prefixes($element) as xs:string*
/// Returns the in-scope namespace prefixes for an element.
/// </summary>
public sealed class InScopePrefixesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "in-scope-prefixes");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "element"), Type = new XdmSequenceType { ItemType = ItemType.Element, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is not XdmElement elem)
            throw new XQueryException("XPTY0004", $"fn:in-scope-prefixes() requires an element node, got {arguments[0]?.GetType().Name ?? "empty sequence"}");

        var prefixes = new HashSet<string>();
        // xml prefix is always in scope
        prefixes.Add("xml");

        // Walk the element and its ancestors to collect ALL in-scope namespace bindings.
        // Stop walking when we encounter a "no-inherit" marker added by a copy-namespaces
        // no-inherit copy — the marker means the element's bindings are self-contained.
        var nodeStore = context.NodeStore;
        XdmNode? current = elem;
        var undeclared = new HashSet<string>();
        while (current != null)
        {
            bool stopAfterThis = false;
            if (current is XdmElement currentElem)
            {
                foreach (var ns in currentElem.NamespaceDeclarations)
                {
                    if (ns.Prefix == Execution.ElementConstructorOperator.NoInheritMarkerPrefix)
                    {
                        stopAfterThis = true;
                        continue;
                    }
                    var prefix = string.IsNullOrEmpty(ns.Prefix) ? "" : ns.Prefix;
                    // A namespace declaration with no URI (NamespaceId.None) and empty prefix
                    // is a namespace UNdeclaration: xmlns="". Don't add it.
                    if (string.IsNullOrEmpty(prefix) && ns.Namespace == NamespaceId.None)
                    {
                        undeclared.Add("");
                        continue;
                    }
                    if (!undeclared.Contains(prefix))
                        prefixes.Add(prefix);
                }
                // Add element's own prefix, but only if the element is actually in a
                // namespace (an element in no namespace contributes no in-scope binding
                // for the default prefix).
                if (!string.IsNullOrEmpty(currentElem.Prefix) && currentElem.Namespace != NamespaceId.None)
                {
                    if (!undeclared.Contains(currentElem.Prefix))
                        prefixes.Add(currentElem.Prefix);
                }
            }
            if (stopAfterThis)
                break;
            current = current.Parent.HasValue && nodeStore != null
                ? nodeStore.GetNode(current.Parent.Value) as XdmNode : null;
        }

        return ValueTask.FromResult<object?>(prefixes.Cast<object?>().ToArray());
    }
}

// ─── fn:path ───────────────────────────────────────────────────────────────

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

/// <summary>
/// fn:path() — 0-arg version uses context item.
/// </summary>
public sealed class Path0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "path");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var node = context.ContextItem as XdmNode;
        if (node is null)
            return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(PathFunction.ComputePath(node, context.NodeStore));
    }
}

// ─── fn:id ─────────────────────────────────────────────────────────────────

/// <summary>
/// fn:id($arg as xs:string*) as element()* — returns elements with matching ID attributes
/// using the context item's document tree.
/// </summary>
public sealed class IdFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "id");
    public override XdmSequenceType ReturnType => new()
    {
        ItemType = ItemType.Element,
        Occurrence = Occurrence.ZeroOrMore
    };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType
            { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var store = context.NodeStore;
        var contextItem = context.ContextItem;
        XdmDocument? doc = null;
        if (contextItem is XdmDocument d)
            doc = d;
        else if (contextItem is XdmNode n && store != null)
            doc = FindDocumentForNode(n, store);
        if (doc == null)
            throw new XQueryException("FODC0001", "FODC0001: No context document for fn:id");

        return ValueTask.FromResult<object?>(FindElementsById(arguments[0], doc, store!));
    }

    /// <summary>
    /// Finds elements whose ID attributes match the given IDREFS values.
    /// </summary>
    internal static object?[] FindElementsById(object? arg, XdmDocument doc, INodeStore store)
    {
        var idValues = new HashSet<string>(StringComparer.Ordinal);
        CollectIdValues(arg, idValues);
        if (idValues.Count == 0)
            return Array.Empty<object?>();

        var results = new List<object?>();
        WalkForIds(doc, idValues, results, store);
        return results.ToArray();
    }

    internal static void CollectIdValues(object? arg, HashSet<string> ids)
    {
        if (arg == null)
            return;
        if (arg is string s)
        {
            foreach (var part in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                ids.Add(part);
        }
        else if (arg is object?[] arr)
        {
            foreach (var item in arr)
                CollectIdValues(item, ids);
        }
        else if (arg is IEnumerable<object?> seq)
        {
            foreach (var item in seq)
                CollectIdValues(item, ids);
        }
        else if (arg is XdmNode node)
        {
            foreach (var part in node.StringValue.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                ids.Add(part);
        }
        else
        {
            ids.Add(arg.ToString()!);
        }
    }

    private static void WalkForIds(XdmNode node, HashSet<string> ids, List<object?> results,
        INodeStore store)
    {
        if (node is XdmElement elem)
        {
            foreach (var attr in store.GetAttributes(elem))
            {
                if (attr.IsId && ids.Contains(attr.Value))
                {
                    results.Add(elem);
                    break; // Only add the element once
                }
            }
            foreach (var childId in elem.Children)
            {
                var child = store.GetNode(childId);
                if (child != null)
                    WalkForIds(child, ids, results, store);
            }
        }
        else if (node is XdmDocument doc)
        {
            foreach (var childId in doc.Children)
            {
                var child = store.GetNode(childId);
                if (child != null)
                    WalkForIds(child, ids, results, store);
            }
        }
    }

    /// <summary>
    /// Walks up the parent chain to find the document node for a given node.
    /// </summary>
    internal static XdmDocument? FindDocumentForNode(XdmNode node, INodeStore store)
    {
        var current = node;
        while (current != null)
        {
            if (current is XdmDocument doc)
                return doc;
            if (current.Parent.HasValue && current.Parent.Value != NodeId.None)
                current = store.GetNode(current.Parent.Value);
            else
                return null;
        }
        return null;
    }
}

/// <summary>
/// fn:id($arg as xs:string*, $node as node()) as element()* — 2-arg version that uses
/// the specified node's document tree.
/// </summary>
public sealed class Id2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "id");
    public override XdmSequenceType ReturnType => new()
    {
        ItemType = ItemType.Element,
        Occurrence = Occurrence.ZeroOrMore
    };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType
            { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore } },
        new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.Node }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var store = context.NodeStore;
        var nodeArg = arguments[1];
        XdmDocument? doc = null;
        if (nodeArg is XdmDocument d)
            doc = d;
        else if (nodeArg is XdmNode n && store != null)
            doc = IdFunction.FindDocumentForNode(n, store);
        if (doc == null)
            throw new XQueryException("FODC0001", "FODC0001: No context document for fn:id");

        return ValueTask.FromResult<object?>(IdFunction.FindElementsById(arguments[0], doc, store!));
    }
}

/// <summary>fn:has-children($node as node()?) as xs:boolean</summary>
public sealed class HasChildrenFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "has-children");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var node = arguments[0];
        if (node == null) return ValueTask.FromResult<object?>(false);
        return ValueTask.FromResult<object?>(HasChildren(node));
    }

    internal static bool HasChildren(object? node) => node switch
    {
        XdmElement e => e.Children != null && e.Children.Count > 0,
        XdmDocument d => d.Children != null && d.Children.Count > 0,
        XdmNode _ => false, // other node types (text, comment, PI, attribute, namespace) have no children
        _ => throw new XQueryException("XPTY0004", "Argument to fn:has-children is not a node")
    };
}

/// <summary>fn:has-children() as xs:boolean (context item)</summary>
public sealed class HasChildren0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "has-children");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext ?? throw new XQueryException("XPDY0002", "Context item absent");
        var item = ctx.ContextItem;
        if (item == null) throw new XQueryException("XPDY0002", "Context item is absent");
        if (item is not XdmNode)
            throw new XQueryException("XPTY0004", "Context item for fn:has-children() is not a node");
        return ValueTask.FromResult<object?>(HasChildrenFunction.HasChildren(item));
    }
}

/// <summary>fn:nilled($arg as node()?) as xs:boolean?</summary>
public sealed class NilledFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "nilled");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var node = arguments[0];
        if (node == null) return ValueTask.FromResult<object?>(null);
        // XPTY0004: argument must be a node
        if (node is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:nilled expects a node, got {node.GetType().Name}");
        // Non-schema-aware: nilled is always false for elements, absent for other node types
        if (node is XdmElement) return ValueTask.FromResult<object?>(false);
        return ValueTask.FromResult<object?>(null);
    }
}

/// <summary>fn:nilled() as xs:boolean? (context item)</summary>
public sealed class Nilled0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "nilled");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext;
        var contextItem = ctx?.ContextItem;
        if (contextItem == null)
            throw new XQueryException("XPDY0002", "Context item is absent");
        // XPTY0004: context item must be a node
        if (contextItem is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:nilled expects a node as context item, got {contextItem.GetType().Name}");
        return new NilledFunction().InvokeAsync([contextItem], context);
    }
}

/// <summary>fn:generate-id($arg as node()?) as xs:string</summary>
public sealed class GenerateIdFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "generate-id");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var node = arguments[0];
        if (node == null) return ValueTask.FromResult<object?>("");
        // XPTY0004: argument must be a node
        if (node is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:generate-id expects a node, got {node.GetType().Name}");
        // Generate a stable ID based on the node's hash code
        var hash = node.GetHashCode();
        return ValueTask.FromResult<object?>($"N{(hash & 0x7FFFFFFF):X8}");
    }
}

/// <summary>fn:generate-id() as xs:string (context item)</summary>
public sealed class GenerateId0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "generate-id");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext;
        var contextItem = ctx?.ContextItem;
        if (contextItem == null)
            throw new XQueryException("XPDY0002", "Context item is absent");
        if (contextItem is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:generate-id expects a node as context item, got {contextItem.GetType().Name}");
        return new GenerateIdFunction().InvokeAsync([contextItem], context);
    }
}

/// <summary>fn:lang($testlang as xs:string?, $node as node()) as xs:boolean</summary>
public sealed class LangFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "lang");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "testlang"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "node"), Type = new() { ItemType = ItemType.Node, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var testLang = arguments[0]?.ToString();
        if (testLang == null) return ValueTask.FromResult<object?>(false);
        var node = arguments[1];
        if (node == null) return ValueTask.FromResult<object?>(false);
        if (node is not XdmNode)
            throw new XQueryRuntimeException("XPTY0004",
                $"Second argument to fn:lang must be a node, got {node.GetType().Name}");

        var nodeStore = context.NodeStore;
        if (nodeStore == null) return ValueTask.FromResult<object?>(false);

        // Walk up ancestors looking for xml:lang attribute
        var current = node as XdmNode;
        while (current != null)
        {
            if (current is XdmElement elem)
            {
                foreach (var attr in nodeStore.GetAttributes(elem))
                {
                    var nsUri = nodeStore.GetNamespaceUri(attr.Namespace);
                    if (attr.LocalName == "lang" &&
                        (nsUri == "http://www.w3.org/XML/1998/namespace" || attr.Prefix == "xml"))
                    {
                        var langVal = attr.StringValue;
                        // Match per BCP 47: testLang matches if langVal equals testLang
                        // or starts with testLang followed by '-'
                        if (langVal.Equals(testLang, StringComparison.OrdinalIgnoreCase) ||
                            langVal.StartsWith(testLang + "-", StringComparison.OrdinalIgnoreCase))
                            return ValueTask.FromResult<object?>(true);
                        return ValueTask.FromResult<object?>(false);
                    }
                }
            }
            if (current.Parent.HasValue)
                current = nodeStore.GetNode(current.Parent.Value) as XdmNode;
            else
                break;
        }
        return ValueTask.FromResult<object?>(false);
    }
}

/// <summary>fn:lang($testlang as xs:string?) as xs:boolean (context item)</summary>
public sealed class Lang1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "lang");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "testlang"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext ?? throw new XQueryException("XPDY0002", "Context item absent");
        var item = ctx.ContextItem ?? throw new XQueryException("XPDY0002", "Context item is absent");
        return new LangFunction().InvokeAsync([arguments[0], item], context);
    }
}

/// <summary>fn:outermost($nodes as node()*) as node()*</summary>
public sealed class OutermostFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "outermost");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "nodes"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        // Return nodes that are not descendants of other nodes in the set, in document order
        var nodes = arguments[0] is IList<object?> list ? list : arguments[0] is IEnumerable<object?> seq ? seq.ToList() : [arguments[0]];

        // Type check: all items must be nodes
        foreach (var item in nodes)
        {
            if (item != null && item is not XdmNode)
                throw new XQueryException("XPTY0004", $"Argument to fn:outermost contains a non-node item of type {item.GetType().Name}");
        }

        if (nodes.Count <= 1) return ValueTask.FromResult(arguments[0]);

        var qec = context as Execution.QueryExecutionContext;
        // Deduplicate by NodeId, preserving first occurrence
        var seen = new HashSet<NodeId>();
        var nodeList = new List<XdmNode>();
        foreach (var n in nodes.OfType<XdmNode>())
        {
            if (seen.Add(n.Id))
                nodeList.Add(n);
        }
        // Build a set of NodeIds in the input for fast lookup
        var nodeIdSet = new HashSet<NodeId>(nodeList.Select(n => n.Id));

        var result = new List<XdmNode>();
        foreach (var node in nodeList)
        {
            bool hasAncestorInSet = false;
            var parentId = node.Parent;
            while (parentId.HasValue && parentId.Value != NodeId.None)
            {
                if (nodeIdSet.Contains(parentId.Value))
                {
                    hasAncestorInSet = true;
                    break;
                }
                // Walk up the parent chain using LoadNode for proper provider resolution
                var parentNode = qec?.LoadNode(parentId.Value) ?? context.NodeStore?.GetNode(parentId.Value);
                parentId = (parentNode as XdmNode)?.Parent;
            }
            if (!hasAncestorInSet)
                result.Add(node);
        }
        // Sort by document order (NodeId)
        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        // Return as object?[] (sequence), not List<object?> (which means XDM array)
        return ValueTask.FromResult<object?>(result.Count == 0 ? null
            : result.Count == 1 ? (object?)result[0] : result.Cast<object?>().ToArray());
    }
}

/// <summary>fn:innermost($nodes as node()*) as node()*</summary>
public sealed class InnermostFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "innermost");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "nodes"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        // Return nodes that have no descendants in the set, in document order
        var nodes = arguments[0] is IList<object?> list ? list : arguments[0] is IEnumerable<object?> seq ? seq.ToList() : [arguments[0]];

        // Type check: all items must be nodes
        foreach (var item in nodes)
        {
            if (item != null && item is not XdmNode)
                throw new XQueryException("XPTY0004", $"Argument to fn:innermost contains a non-node item of type {item.GetType().Name}");
        }

        if (nodes.Count <= 1) return ValueTask.FromResult(arguments[0]);

        var qec = context as Execution.QueryExecutionContext;
        // Deduplicate by NodeId, preserving first occurrence
        var seen = new HashSet<NodeId>();
        var nodeList = new List<XdmNode>();
        foreach (var n in nodes.OfType<XdmNode>())
        {
            if (seen.Add(n.Id))
                nodeList.Add(n);
        }
        // Build a set of NodeIds in the input for fast lookup
        var nodeIdSet = new HashSet<NodeId>(nodeList.Select(n => n.Id));

        var result = new List<XdmNode>();
        foreach (var node in nodeList)
        {
            bool hasDescendantInSet = false;
            // Check if any other node in the set has this node as an ancestor
            foreach (var other in nodeList)
            {
                if (other.Id == node.Id) continue;
                var parentId = other.Parent;
                while (parentId.HasValue && parentId.Value != NodeId.None)
                {
                    if (parentId.Value == node.Id)
                    {
                        hasDescendantInSet = true;
                        break;
                    }
                    // Walk up the parent chain using LoadNode for proper provider resolution
                    var parentNode = qec?.LoadNode(parentId.Value) ?? context.NodeStore?.GetNode(parentId.Value);
                    parentId = (parentNode as XdmNode)?.Parent;
                }
                if (hasDescendantInSet) break;
            }
            if (!hasDescendantInSet)
                result.Add(node);
        }
        // Sort by document order (NodeId)
        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        // Return as object?[] (sequence), not List<object?> (which means XDM array)
        return ValueTask.FromResult<object?>(result.Count == 0 ? null
            : result.Count == 1 ? (object?)result[0] : result.Cast<object?>().ToArray());
    }
}

/// <summary>fn:document-uri() as xs:anyURI? (0-arg uses context item)</summary>
public sealed class DocumentUri0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "document-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext ?? throw new XQueryException("XPDY0002", "Context item absent");
        return new DocumentUriFunction().InvokeAsync([ctx.ContextItem], context);
    }
}

/// <summary>fn:idref($arg as xs:string*, $node as node()) as node()*</summary>
public sealed class IdrefFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "idref");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "node"), Type = new() { ItemType = ItemType.Node, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        // FODC0001: node argument must be from a tree whose root is a document node
        var node = arguments[1];
        if (node is PhoenixmlDb.Xdm.Nodes.XdmNode xn && xn is not PhoenixmlDb.Xdm.Nodes.XdmDocument)
        {
            // If the node has no parent, it is not rooted at a document
            if (xn.Parent is null)
                throw new XQueryRuntimeException("FODC0001",
                    "fn:idref: node is not in a tree rooted at a document node");
        }
        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}

/// <summary>fn:idref($arg as xs:string*) as node()* (context item as node)</summary>
public sealed class Idref1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "idref");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        // FODC0001: context node must be from a tree rooted at a document node
        if (context is Execution.QueryExecutionContext qec &&
            qec.ContextItem is PhoenixmlDb.Xdm.Nodes.XdmNode ctxNode &&
            ctxNode is not PhoenixmlDb.Xdm.Nodes.XdmDocument &&
            ctxNode.Parent is null)
        {
            throw new XQueryRuntimeException("FODC0001",
                "fn:idref: context node is not in a tree rooted at a document node");
        }
        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}

/// <summary>fn:element-with-id($arg as xs:string*, $node as node()) as element()*</summary>
public sealed class ElementWithId2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "element-with-id");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "node"), Type = new() { ItemType = ItemType.Node, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}

/// <summary>fn:element-with-id($arg as xs:string*) as element()*</summary>
public sealed class ElementWithId1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "element-with-id");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}
