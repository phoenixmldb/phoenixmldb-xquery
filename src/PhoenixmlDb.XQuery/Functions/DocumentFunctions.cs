using System.Text.Json;
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

// ═══════════════════════════════════════════════════════════════════════════
// JSON document functions (XPath 3.1 §14.8)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Helper to convert System.Text.Json elements to XDM maps, arrays, and atomic values.
/// </summary>
internal static class JsonToXdmConverter
{
    /// <summary>
    /// Converts a <see cref="JsonElement"/> to its XDM representation:
    /// objects → Dictionary&lt;object, object?&gt; (XDM map),
    /// arrays → List&lt;object?&gt; (XDM array),
    /// strings → string, numbers → decimal or double, booleans → bool, null → null (empty sequence).
    /// </summary>
    internal static object? Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var map = new Dictionary<object, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    map[prop.Name] = Convert(prop.Value);
                }
                return map;
            }

            case JsonValueKind.Array:
            {
                var array = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(Convert(item));
                }
                return array;
            }

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                // Prefer decimal for exact numeric values; fall back to double for
                // values outside decimal range (e.g. very large exponents).
                if (element.TryGetDecimal(out var dec))
                    return dec;
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }
}

/// <summary>
/// fn:parse-json($json-text as xs:string) as item()?
/// Parses a JSON string and returns the corresponding XDM value
/// (map for objects, array for arrays, atomic for primitives).
/// </summary>
public sealed class ParseJsonFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-json");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var jsonText = arguments[0]?.ToString();
        if (jsonText == null)
            return ValueTask.FromResult<object?>(null);

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            // JsonDocument is disposable; we must materialize the result before disposing.
            var result = JsonToXdmConverter.Convert(doc.RootElement);
            return ValueTask.FromResult(result);
        }
        catch (JsonException ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"The string supplied to fn:parse-json() is not valid JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// fn:parse-json($json-text as xs:string, $options as map(*)) as item()?
/// </summary>
public sealed class ParseJson2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-json");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "options"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Ignore options for now — just parse the JSON
        return new ParseJsonFunction().InvokeAsync([arguments[0]], context);
    }
}

/// <summary>
/// fn:json-doc($href as xs:string) as item()?
/// Reads a JSON file from the given URI and returns the corresponding XDM value.
/// </summary>
public sealed class JsonDocFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "json-doc");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString }];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var href = arguments[0]?.ToString();
        if (href == null)
            return null;

        // Resolve relative URI against static base URI
        if (context is QueryExecutionContext queryContext && queryContext.StaticBaseUri != null)
        {
            if (!Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                    href = new Uri(baseUri, href).AbsoluteUri;
            }
        }

        string jsonText;
        try
        {
            // Support file:// URIs and plain file paths
            var filePath = href;
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile)
                filePath = uri.LocalPath;

            jsonText = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"Error reading JSON resource '{href}': {ex.Message}");
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var result = JsonToXdmConverter.Convert(doc.RootElement);
            return result;
        }
        catch (JsonException ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"The resource '{href}' does not contain valid JSON: {ex.Message}");
        }
    }
}

/// <summary>fn:json-doc($href, $options) as item()?</summary>
public sealed class JsonDoc2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "json-doc");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "options"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return new JsonDocFunction().InvokeAsync([arguments[0]], context);
    }
}

/// <summary>fn:load-xquery-module($module-uri as xs:string) as map(*)</summary>
public sealed class LoadXQueryModuleFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "load-xquery-module");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "module-uri"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Stub: return empty map for now
        throw new XQueryRuntimeException("FOQM0006",
            $"Module '{arguments[0]}' cannot be loaded");
    }
}

/// <summary>fn:load-xquery-module($module-uri, $options) as map(*)</summary>
public sealed class LoadXQueryModule2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "load-xquery-module");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "module-uri"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "options"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return new LoadXQueryModuleFunction().InvokeAsync([arguments[0]], context);
    }
}
