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
        var c = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PhoenixmlDb.XQuery");
        return c;
    }

    /// <summary>
    /// Opens a streaming read of <paramref name="uri"/>. The caller is responsible for
    /// disposing the returned stream. Streaming avoids buffering large documents into a
    /// string before parsing — important for the 100+ MB documents the engine targets.
    /// </summary>
    public static Stream OpenRead(Uri uri)
    {
        var response = _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
    }
}
