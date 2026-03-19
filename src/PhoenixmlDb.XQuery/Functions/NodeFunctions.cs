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
                elem.Prefix != null ? $"{elem.Prefix}:{elem.LocalName}" : elem.LocalName),
            XdmAttribute attr => ValueTask.FromResult<object?>(
                attr.Prefix != null ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName),
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
                elem.Prefix != null ? $"{elem.Prefix}:{elem.LocalName}" : elem.LocalName),
            XdmAttribute attr => ValueTask.FromResult<object?>(
                attr.Prefix != null ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName),
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

        var resolver = (context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext)?.NamespaceResolver;
        return arg switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(ResolveNsId(elem.Namespace, resolver)),
            XdmAttribute attr => ValueTask.FromResult<object?>(ResolveNsId(attr.Namespace, resolver)),
            XdmNamespace ns => ValueTask.FromResult<object?>(ns.Uri),
            _ => ValueTask.FromResult<object?>("")
        };
    }

    internal static string ResolveNsId(NamespaceId id, Func<NamespaceId, string?>? resolver)
    {
        if (id == NamespaceId.None) return "";
        return resolver?.Invoke(id) ?? "";
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

        var resolver = (context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext)?.NamespaceResolver;
        return item switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(NamespaceUriFunction.ResolveNsId(elem.Namespace, resolver)),
            XdmAttribute attr => ValueTask.FromResult<object?>(NamespaceUriFunction.ResolveNsId(attr.Namespace, resolver)),
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
        if (arg is not XdmNode node)
            return ValueTask.FromResult<object?>(null);

        // Traverse to root
        var current = node;
        while (current.Parent != NodeId.None)
        {
            // Would need node loader to get parent
            // For now, just return the node itself
            break;
        }

        return ValueTask.FromResult<object?>(current);
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
        // Would use context item
        return ValueTask.FromResult<object?>(null);
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
                if (attr != null && attr.Namespace == NamespaceId.Xml && attr.LocalName == "base")
                {
                    xmlBase = attr.Value;
                    break;
                }
            }

            if (xmlBase != null)
            {
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
            XdmNamespace ns => null, // namespace nodes have no name per spec
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
        object? arg = null;
        if (context is Execution.QueryExecutionContext qec)
            arg = qec.ContextItem;
        return ValueTask.FromResult<object?>(NodeNameFunction.NodeNameOf(arg, context));
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

        var resolver = (context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext)?.NamespaceResolver;
        foreach (var ns in element.NamespaceDeclarations)
        {
            if (ns.Prefix == prefix || (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(ns.Prefix)))
                return ValueTask.FromResult<object?>(NamespaceUriFunction.ResolveNsId(ns.Namespace, resolver));
        }
        if (element.Prefix == prefix)
            return ValueTask.FromResult<object?>(NamespaceUriFunction.ResolveNsId(element.Namespace, resolver));
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
            if (prefix != null)
            {
                // Search namespace declarations for the given prefix
                string? found = null;
                foreach (var nsDecl in xdmElem.NamespaceDeclarations)
                {
                    if (nsDecl.Prefix == prefix)
                    {
                        found = nsResolver?.Invoke(nsDecl.Namespace) ?? "";
                        break;
                    }
                }
                if (found == null)
                    throw new InvalidOperationException($"FONS0004: Prefix '{prefix}' is not declared in the in-scope namespaces");
                namespaceUri = found;
            }
            else
            {
                // No prefix: use the default namespace (empty prefix binding)
                namespaceUri = "";
                foreach (var nsDecl in xdmElem.NamespaceDeclarations)
                {
                    if (string.IsNullOrEmpty(nsDecl.Prefix))
                    {
                        namespaceUri = nsResolver?.Invoke(nsDecl.Namespace) ?? "";
                        break;
                    }
                }
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
        [new() { Name = new QName(NamespaceId.None, "element"), Type = XdmSequenceType.Node }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is not XdmElement elem)
            return ValueTask.FromResult<object?>(Array.Empty<object?>());

        var prefixes = new List<object?>();
        // xml prefix is always in scope
        prefixes.Add("xml");
        // Add default namespace prefix (empty string) if element has a namespace
        foreach (var ns in elem.NamespaceDeclarations)
        {
            var prefix = string.IsNullOrEmpty(ns.Prefix) ? "" : ns.Prefix;
            if (!prefixes.Contains(prefix))
                prefixes.Add(prefix);
        }
        // Add element's own prefix if not already present
        if (elem.Prefix != null && !prefixes.Contains(elem.Prefix))
            prefixes.Add(elem.Prefix);

        return ValueTask.FromResult<object?>(prefixes.ToArray());
    }
}
