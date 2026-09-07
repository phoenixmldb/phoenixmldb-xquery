using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

            // Translate a mapped resource URI (e.g. a catalog http:// URI used by a test
            // harness, or any application-registered logical URI) to its backing local
            // file path before delegating to the resolver. Without this, a doc('rel.xml')
            // that resolves against an http:// static base URI would attempt a network
            // fetch instead of reading the registered local resource.
            uri = ResourceUriResolver.Map(queryContext, uri);

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
