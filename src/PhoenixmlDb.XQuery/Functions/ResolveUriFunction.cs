using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:resolve-uri($relative, $base) as xs:anyURI?
/// </summary>
public sealed class ResolveUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "resolve-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalAnyUri;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "relative"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "base"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var relative = arguments[0]?.ToString();
        if (relative == null)
            return ValueTask.FromResult<object?>(null);

        var baseUri = arguments[1]?.ToString() ?? "";

        // If the relative URI is already absolute (has a scheme component), return it directly.
        // Note: .NET's Uri.TryCreate with UriKind.Absolute also accepts path-absolute forms like "/foo/bar"
        // which are NOT RFC 3986 absolute URIs (they have no scheme). We must check for a scheme explicitly.
        if (relative.Contains(':') && Uri.TryCreate(relative, UriKind.Absolute, out var absUri)
            && absUri.Scheme.Length > 0)
            return ValueTask.FromResult<object?>(new Xdm.XsAnyUri(absUri.OriginalString));

        // FORG0002: base URI must be a valid absolute URI (must contain a scheme with ':')
        if (baseUri.Contains("##"))
            throw new XQueryRuntimeException("FORG0002", $"The base URI '{baseUri}' is not a valid URI");
        if (!baseUri.Contains(':') || !Uri.TryCreate(baseUri, UriKind.Absolute, out var baseUriObj))
            throw new XQueryRuntimeException("FORG0002", $"The base URI '{baseUri}' is not a valid absolute URI");

        // FORG0002: base URI must not contain a fragment (RFC 3986 §5.2)
        if (baseUri.Contains('#'))
            throw new XQueryRuntimeException("FORG0002", $"The base URI '{baseUri}' must not contain a fragment identifier");

        // FORG0002: relative URI must be a valid URI reference
        if (relative.Contains("##"))
            throw new XQueryRuntimeException("FORG0002", $"The relative URI '{relative}' is not a valid URI reference");
        // A colon in the first path segment (before any slash) makes the relative URI invalid
        // (RFC 3986 §3.3: it would be ambiguous with a scheme)
        {
            var firstSlash = relative.IndexOf('/');
            var firstColon = relative.IndexOf(':');
            if (firstColon >= 0 && (firstSlash < 0 || firstColon < firstSlash))
                throw new XQueryRuntimeException("FORG0002", $"The relative URI '{relative}' is not a valid URI reference (colon in first path segment)");
        }

        try
        {
            if (Uri.TryCreate(baseUriObj, relative, out var resolved))
            {
                // Use OriginalString instead of AbsoluteUri: .NET normalizes "http://g" → "http://g/"
                // (adds trailing slash for empty path), but OriginalString preserves the correct form.
                return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(resolved.OriginalString));
            }
            throw new XQueryRuntimeException("FORG0002", $"Cannot resolve URI '{relative}' against base '{baseUri}'");
        }
        catch (XQueryRuntimeException) { throw; }
        catch (UriFormatException)
        {
            throw new XQueryRuntimeException("FORG0002", $"Cannot resolve URI '{relative}' against base '{baseUri}'");
        }
    }
}
