using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

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

        if (context is QueryExecutionContext queryContext)
        {
            // Resolve a RELATIVE URI against the static base URI, when there is one.
            if (queryContext.StaticBaseUri != null && !Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                    href = new Uri(baseUri, href).AbsoluteUri;
            }

            // Translate a registered resource URI (e.g. a logical http:// URI bound to a local
            // file) to its backing file:// path. This must run REGARDLESS of the static base
            // URI: a registered URI is typically already absolute, so it needs no rebasing, and
            // gating the lookup on StaticBaseUri != null meant a host that registered resources
            // without also setting a base URI got no mapping at all — json-doc then treated
            // "http://…/mapEmpty-json" as a literal path and reported it could not find
            // "…/bin/Debug/net10.0/http:".
            href = ResourceUriResolver.Map(queryContext, href);
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
            var processed = JsonToXdmConverter.PreProcessSurrogates(jsonText);
            using var doc = JsonDocument.Parse(processed);
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
