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

    /// <summary>
    /// A relative reference whose first path segment contains a colon is not a URI reference
    /// (RFC 3986 §4.2: that segment would read as a scheme), though System.Uri accepts it as
    /// relative: doc(':/') looked for a file named ':' (QT3 K2-SeqDocFunc-14).
    /// </summary>
    private static bool IsColonInFirstRelativeSegment(string uri)
    {
        var end = uri.IndexOfAny(['/', '?', '#']);
        var first = end < 0 ? uri : uri[..end];
        var colon = first.IndexOf(':', StringComparison.Ordinal);
        // scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ): before such a colon the reference
        // is absolute (file:/abs/path, urn:a:b).
        return colon >= 0 && !IsScheme(first.AsSpan(0, colon));
    }

    private static bool IsScheme(ReadOnlySpan<char> s)
    {
        if (s.Length == 0 || !char.IsAsciiLetter(s[0]))
            return false;
        foreach (var c in s)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                return false;
        }
        return true;
    }

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();
        if (uri == null)
            return ValueTask.FromResult<object?>(null);

        // Validate URI syntax — FODC0005 for invalid URIs
        if (uri.Length > 0 && (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out _) || IsColonInFirstRelativeSegment(uri)))
            throw new XQueryRuntimeException("FODC0005",
                $"The URI '{uri}' passed to fn:doc() is not a valid URI");

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            var caller = Security.ResourceGate.Caller(queryContext);
            // Resolve against the static base URI of the calling module (XSLT 3.0 §13.2).
            // doc('') returns the module itself; relative URIs resolve against its location.
            if (queryContext.StaticBaseUri != null)
            {
                if (uri.Length == 0)
                    uri = queryContext.StaticBaseUri;
            }
            if (uri.Length > 0)
            {
                uri = LocationResolver.Absolute(uri, queryContext.StaticBaseUri)
                    ?? throw new XQueryRuntimeException("FODC0005",
                        $"The URI '{uri}' passed to fn:doc() cannot be resolved against the base URI");
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
                doc = Security.ResourceGate.ResolveDocument(queryContext.DocumentResolver, uri, caller);
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
