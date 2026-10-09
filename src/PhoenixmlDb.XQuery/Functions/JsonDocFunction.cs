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

        string? hostSupplied = null;
        if (context is QueryExecutionContext queryContext)
        {
            // Resolve a RELATIVE URI against the static base URI, when there is one.
            href = LocationResolver.Absolute(href, queryContext.StaticBaseUri)
                ?? throw new XQueryRuntimeException("FOUT1170", $"Cannot retrieve '{href}': it cannot be resolved against the base URI");

            // Translate a registered resource URI (e.g. a logical http:// URI bound to a local
            // file) to its backing file:// path. This must run REGARDLESS of the static base
            // URI: a registered URI is typically already absolute, so it needs no rebasing, and
            // gating the lookup on StaticBaseUri != null meant a host that registered resources
            // without also setting a base URI got no mapping at all — json-doc then treated
            // "http://…/mapEmpty-json" as a literal path and reported it could not find
            // "…/bin/Debug/net10.0/http:".
            // The policy judges the resource requested (before a host mapping swaps in a file),
            // and a file is read at the canonical path it authorised. json-doc's retrieval
            // errors are unparsed-text's: FOUT1170.
            // The host's own content first: with it, nothing is authorised by name and then
            // opened, so there is no window for the file to be replaced in between.
            hostSupplied = Security.ResourceGate.HostContent(context, href, Security.ResourceAccessKind.ReadText, "FOUT1170")?.ReadText();
            var authorized = hostSupplied != null ? null
                : Security.ResourceGate.Authorize(context, href, Security.ResourceAccessKind.ReadText, "FOUT1170");
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

        var jsonText = hostSupplied ?? await JsonDocFunction.ReadJsonResourceAsync(href).ConfigureAwait(false);

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

    /// <summary>
    /// Reads a json-doc resource as fn:unparsed-text would (F&amp;O 3.1 §17.5.4): UTF-8 unless a byte
    /// order mark says otherwise, decoded strictly. A byte sequence that is not valid in that
    /// inferred encoding is FOUT1200 (FOUT1190 is for an encoding the caller named, which json-doc
    /// never takes), and a resource that cannot be retrieved is FOUT1170.
    /// </summary>
    /// <remarks>
    /// The file was decoded leniently, so invalid UTF-8 became U+FFFD and reached the JSON parser,
    /// which reported a JSON syntax error (FOJS0001) about a character the file does not contain
    /// (QT3 misc-JsonTestSuite, 12 cases). Every retrieval error was also reported as FOJS0001.
    /// </remarks>
    internal static async Task<string> ReadJsonResourceAsync(string href)
    {
        var filePath = href;
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile)
            filePath = uri.LocalPath;
        try
        {
            return await File.ReadAllTextAsync(filePath,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)).ConfigureAwait(false);
        }
        catch (System.Text.DecoderFallbackException ex)
        {
            throw new XQueryRuntimeException("FOUT1200",
                $"The resource '{href}' contains bytes that are not valid in its encoding: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new XQueryRuntimeException("FOUT1170", $"Cannot retrieve the resource '{href}': {ex.Message}");
        }
    }
}
