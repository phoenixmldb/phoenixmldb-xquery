using System.Net.Http;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Shared <see cref="HttpClient"/> for fetching XML documents referenced by
/// <c>fn:doc()</c> / <c>xsl:source-document</c> / <c>fn:doc-available()</c> when the
/// resolved URI is <c>http://</c> or <c>https://</c>.
/// </summary>
/// <remarks>
/// One static client is reused across the process to amortize TCP/TLS handshake cost.
/// Conservative 30 s timeout — long enough for typical XML documents over the network,
/// short enough that a misconfigured URL surfaces as an error rather than hanging the
/// query. Sandbox enforcement is the caller's responsibility:
/// <c>PolicyEnforcingResolver</c> wraps this when a <c>ResourcePolicy</c> is configured.
/// </remarks>
internal static class HttpDocumentClient
{
    private static readonly HttpClient _client = CreateClient();

    private static HttpClient CreateClient()
    {
        // Redirects are followed by hand (OpenRead), so each hop can be re-authorised: with
        // automatic redirects an allowed origin could send the fetch to any host or port.
        // The handler lives as long as the process-wide client that owns it. On WebAssembly there
        // is no SocketsHttpHandler, and OpenRead refuses to run there anyway.
#pragma warning disable CA2000
        var c = OperatingSystem.IsBrowser()
            ? new HttpClient()
            : new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }, disposeHandler: true);
#pragma warning restore CA2000
        c.Timeout = TimeSpan.FromSeconds(30);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PhoenixmlDb.XQuery");
        return c;
    }

    private const int MaxRedirects = 10;

    /// <summary>
    /// Opens a streaming read of <paramref name="uri"/>. The caller is responsible for
    /// disposing the returned stream. Streaming avoids buffering large documents into a
    /// string before parsing — important for the 100+ MB documents the engine targets.
    /// </summary>
    /// <remarks>
    /// Synchronous-over-async: the underlying <see cref="HttpClient"/> call is awaited
    /// to completion on the calling thread. This is fine on runtimes that allow thread
    /// blocking (server, desktop, CLI) but fails on Blazor WebAssembly (single-threaded,
    /// cannot park a thread on a monitor wait). On WASM, register a custom
    /// <see cref="IDocumentResolver"/> that pre-fetches documents asynchronously instead
    /// of relying on this default loader — we throw a clear error here rather than the
    /// runtime's obscure <c>Cannot wait on monitors</c> message.
    /// </remarks>
    public static Stream OpenRead(Uri uri) => OpenRead(uri, authorizeRedirect: null);

    /// <summary>
    /// As <see cref="OpenRead(Uri)"/>, calling <paramref name="authorizeRedirect"/> with every
    /// redirect target before following it. Null follows redirects unchecked (no policy).
    /// </summary>
    /// <exception cref="Security.ResourceAccessDeniedException">A redirect target was refused.</exception>
    public static Stream OpenRead(Uri uri, Func<Uri, bool>? authorizeRedirect)
    {
        if (OperatingSystem.IsBrowser())
        {
            throw new System.IO.IOException(
                $"Cannot fetch '{uri}' on Blazor WebAssembly via the default HTTP loader: " +
                "synchronous HTTP I/O is not supported here. Register a custom IDocumentResolver " +
                "that pre-fetches documents asynchronously (e.g. via JS interop or HttpClient with await), " +
                "or — when calling from XSLT — pass content through PreloadedResources on LoadStylesheetAsync.");
        }
        var current = uri;
        for (var hop = 0; ; hop++)
        {
            var response = _client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            if (NextHop(current, response) is not { } next)
            {
                response.EnsureSuccessStatusCode();
                return response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            }
            response.Dispose();
            current = CheckHop(uri, next, hop, authorizeRedirect);
        }
    }

    /// <summary>Async form of <see cref="OpenRead(Uri, Func{Uri, bool}?)"/>, reading the body as text.</summary>
    public static async Task<string> GetStringAsync(Uri uri, Func<Uri, bool>? authorizeRedirect, CancellationToken cancellationToken = default)
    {
        var current = uri;
        for (var hop = 0; ; hop++)
        {
            using var response = await _client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (NextHop(current, response) is not { } next)
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            current = CheckHop(uri, next, hop, authorizeRedirect);
        }
    }

    private static Uri? NextHop(Uri current, HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is not { } location)
            return null;
        return location.IsAbsoluteUri ? location : new Uri(current, location);
    }

    private static Uri CheckHop(Uri original, Uri next, int hop, Func<Uri, bool>? authorizeRedirect)
    {
        if (hop >= MaxRedirects)
            throw new HttpRequestException($"Too many redirects fetching '{original}'");
        // Under a policy the refusal names what was asked for and not where the server sent
        // it: the target of a redirect is the server's answer, and a query that catches the
        // error must not learn from it an address it was never allowed to reach.
        if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps)
            throw new HttpRequestException(authorizeRedirect != null
                ? $"A redirect from '{original}' to a non-HTTP URI is not followed"
                : $"Redirect from '{original}' to a non-HTTP URI '{next}' is not followed");
        if (authorizeRedirect != null && !authorizeRedirect(next))
            throw new Security.ResourceAccessDeniedException(original.AbsoluteUri, Security.ResourceAccessKind.ReadDocument,
                "it redirects to a location the resource policy does not allow");
        return next;
    }
}
