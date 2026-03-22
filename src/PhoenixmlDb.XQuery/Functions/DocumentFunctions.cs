using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:doc($uri) as document-node()?
/// </summary>
public sealed class DocFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "doc");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Document, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "uri"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();
        if (uri == null)
            return ValueTask.FromResult<object?>(null);

        // Validate URI syntax — FODC0005 for invalid URIs
        if (uri.Length > 0 && !Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out _))
            throw new XQueryRuntimeException("FODC0005",
                $"The URI '{uri}' passed to fn:doc() is not a valid URI");

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            // Resolve against the static base URI of the calling module (XSLT 3.0 §13.2).
            // doc('') returns the module itself; relative URIs resolve against its location.
            if (queryContext.StaticBaseUri != null)
            {
                if (uri.Length == 0)
                    uri = queryContext.StaticBaseUri;
                else if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                {
                    if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                        uri = new Uri(baseUri, uri).AbsoluteUri;
                }
            }

            object? doc;
            try
            {
                doc = queryContext.DocumentResolver.ResolveDocument(uri);
            }
            catch (Exception ex)
            {
                throw new XQueryRuntimeException("FODC0002",
                    $"Error retrieving resource identified by URI '{uri}': {ex.Message}");
            }

            if (doc == null)
                throw new XQueryRuntimeException("FODC0002",
                    $"No document could be retrieved for URI '{uri}'");

            return ValueTask.FromResult<object?>(doc);
        }

        // No document resolver available — cannot retrieve any document
        throw new XQueryRuntimeException("FODC0002",
            $"No document resolver is available to retrieve URI '{uri}'");
    }
}

/// <summary>
/// fn:doc-available($uri) as xs:boolean
/// </summary>
public sealed class DocAvailableFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "doc-available");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "uri"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();
        if (uri == null)
            return ValueTask.FromResult<object?>(false);

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            if (queryContext.StaticBaseUri != null)
            {
                if (uri.Length == 0)
                    uri = queryContext.StaticBaseUri;
                else if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                {
                    if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                        uri = new Uri(baseUri, uri).AbsoluteUri;
                }
            }
            var available = queryContext.DocumentResolver.IsDocumentAvailable(uri);
            return ValueTask.FromResult<object?>(available);
        }

        return ValueTask.FromResult<object?>(false);
    }
}

/// <summary>
/// fn:collection($arg) as node()*
/// </summary>
public sealed class CollectionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Node, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            var docs = queryContext.DocumentResolver.ResolveCollection(uri).ToArray();
            return ValueTask.FromResult<object?>(docs);
        }

        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}

/// <summary>
/// fn:collection() as node()*  (default collection)
/// </summary>
public sealed class Collection0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Node, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            var docs = queryContext.DocumentResolver.ResolveCollection(null).ToArray();
            return ValueTask.FromResult<object?>(docs);
        }

        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}

/// <summary>
/// fn:uri-collection($arg as xs:string?) as xs:anyURI*
/// Returns the URIs of documents in a collection.
/// </summary>
public sealed class UriCollectionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "uri-collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            var docs = queryContext.DocumentResolver.ResolveCollection(uri);
            var uris = new List<object?>();
            foreach (var doc in docs)
            {
                string? docUri = null;
                if (doc is Xdm.Nodes.XdmDocument xdmDoc)
                    docUri = xdmDoc.DocumentUri;
                else if (doc is Xdm.Nodes.XdmNode node)
                    docUri = node.BaseUri;

                if (docUri != null)
                    uris.Add(new Xdm.XsAnyUri(docUri));
            }
            return ValueTask.FromResult<object?>(uris.ToArray());
        }

        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}

/// <summary>
/// fn:uri-collection() as xs:anyURI*  (default collection)
/// </summary>
public sealed class UriCollection0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "uri-collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            var docs = queryContext.DocumentResolver.ResolveCollection(null);
            var uris = new List<object?>();
            foreach (var doc in docs)
            {
                string? docUri = null;
                if (doc is Xdm.Nodes.XdmDocument xdmDoc)
                    docUri = xdmDoc.DocumentUri;
                else if (doc is Xdm.Nodes.XdmNode node)
                    docUri = node.BaseUri;

                if (docUri != null)
                    uris.Add(new Xdm.XsAnyUri(docUri));
            }
            return ValueTask.FromResult<object?>(uris.ToArray());
        }

        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}
