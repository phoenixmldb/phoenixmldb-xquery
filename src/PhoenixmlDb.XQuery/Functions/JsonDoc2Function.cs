using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

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

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Parse and validate options
        ParseJsonOptions? opts = null;
        if (arguments[1] is IDictionary<object, object?> map)
            opts = ParseJsonOptions.FromMap(map);

        var href = arguments[0]?.ToString();
        if (href == null)
            return null;

        if (context is QueryExecutionContext queryContext)
        {
            if (queryContext.StaticBaseUri != null && !Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                    href = new Uri(baseUri, href).AbsoluteUri;
            }

            // The single-argument overload above maps registered resource URIs; this one did
            // not, so json-doc($uri) and json-doc($uri, $options) disagreed about whether a
            // host-registered URI resolves.
            // The policy judges the resource requested (before a host mapping swaps in a file),
            // and a file is read at the canonical path it authorised. json-doc's retrieval
            // errors are unparsed-text's: FOUT1170.
            var authorized = Security.ResourceGate.Authorize(context, href, Security.ResourceAccessKind.ReadText, "FOUT1170");
            var mapped = ResourceUriResolver.Map(queryContext, href);
            if (authorized != null && mapped == href)
            {
                if (!authorized.IsFile)
                    throw new XQueryRuntimeException("FOUT1170", $"Cannot retrieve '{href}': only file resources are read by fn:json-doc");
                mapped = authorized.LocalPath;
            }
            href = mapped;
        }
        else if (context.ResourcePolicy != null)
        {
            throw new XQueryRuntimeException("FOUT1170", $"Cannot retrieve '{href}' under a resource policy outside a query context");
        }

        string jsonText;
        try
        {
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
            var result = JsonToXdmConverter.Convert(doc.RootElement, opts, context);
            return result;
        }
        catch (JsonException ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"The resource '{href}' does not contain valid JSON: {ex.Message}");
        }
    }
}
