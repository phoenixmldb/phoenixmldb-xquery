using System.Text;
using System.Text.RegularExpressions;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// URI reference resolution exactly as RFC 3986 §5.2 specifies it, for fn:resolve-uri.
/// </summary>
/// <remarks>
/// System.Uri resolution normalises and re-escapes in ways the RFC does not (and fn:resolve-uri
/// used its OriginalString to undo some of it). It kept dot segments that climb past the root:
/// resolve-uri('/./././', 'http://example.com/a/') came out as 'http://example.com/./././' and
/// '/..' as 'http://example.com/..', where RFC 3986's remove_dot_segments gives
/// 'http://example.com/' (W3C XSLT resolve-uri-022/023). This works on the strings alone:
/// no case folding, no escaping or unescaping, no scheme-specific rules.
/// </remarks>
internal static partial class Rfc3986
{
    // RFC 3986 Appendix B: the regular expression that splits any URI reference into its parts.
    [GeneratedRegex(@"^(([^:/?#]+):)?(//([^/?#]*))?([^?#]*)(\?([^#]*))?(#(.*))?$", RegexOptions.Singleline)]
    private static partial Regex Components();

    private readonly record struct Parts(string? Scheme, string? Authority, string Path, string? Query, string? Fragment);

    private static Parts Split(string reference)
    {
        var m = Components().Match(reference);
        return new Parts(
            m.Groups[1].Success ? m.Groups[2].Value : null,
            m.Groups[3].Success ? m.Groups[4].Value : null,
            m.Groups[5].Value,
            m.Groups[6].Success ? m.Groups[7].Value : null,
            m.Groups[8].Success ? m.Groups[9].Value : null);
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex SchemePrefix();

    /// <summary>
    /// True when <paramref name="reference"/> begins with a scheme in RFC 3986 §3.1 syntax, i.e.
    /// is an absolute URI (which fn:resolve-uri returns unchanged).
    /// </summary>
    internal static bool IsAbsolute(string reference) => SchemePrefix().IsMatch(reference);

    /// <summary>True when <paramref name="uri"/> has a scheme, i.e. can serve as a base (§5.1).</summary>
    internal static bool HasScheme(string uri) => Split(uri).Scheme is { Length: > 0 };

    /// <summary>
    /// Resolves <paramref name="reference"/> against the absolute <paramref name="baseUri"/>
    /// (RFC 3986 §5.2.2, strict parser).
    /// </summary>
    internal static string Resolve(string baseUri, string reference)
    {
        var b = Split(baseUri);
        var r = Split(reference);
        string? scheme, authority, query;
        string path;

        if (r.Scheme != null)
        {
            scheme = r.Scheme; authority = r.Authority; path = RemoveDotSegments(r.Path); query = r.Query;
        }
        else
        {
            if (r.Authority != null)
            {
                authority = r.Authority; path = RemoveDotSegments(r.Path); query = r.Query;
            }
            else
            {
                if (r.Path.Length == 0)
                {
                    path = b.Path;
                    query = r.Query ?? b.Query;
                }
                else
                {
                    path = r.Path.StartsWith('/') ? RemoveDotSegments(r.Path) : RemoveDotSegments(Merge(b, r.Path));
                    query = r.Query;
                }
                authority = b.Authority;
            }
            scheme = b.Scheme;
        }

        // §5.3 component recomposition.
        var sb = new StringBuilder();
        if (scheme != null) sb.Append(scheme).Append(':');
        if (authority != null) sb.Append("//").Append(authority);
        sb.Append(path);
        if (query != null) sb.Append('?').Append(query);
        if (r.Fragment != null) sb.Append('#').Append(r.Fragment);
        return sb.ToString();
    }

    // §5.2.3: merge a relative-path reference with the base path.
    private static string Merge(Parts b, string referencePath)
    {
        if (b.Authority != null && b.Path.Length == 0)
            return "/" + referencePath;
        var lastSlash = b.Path.LastIndexOf('/');
        return lastSlash < 0 ? referencePath : b.Path[..(lastSlash + 1)] + referencePath;
    }

    // §5.2.4: remove_dot_segments, as the RFC's step-by-step algorithm.
    internal static string RemoveDotSegments(string path)
    {
        var input = path;
        var output = new StringBuilder();
        while (input.Length > 0)
        {
            if (input.StartsWith("../", StringComparison.Ordinal)) input = input[3..];
            else if (input.StartsWith("./", StringComparison.Ordinal)) input = input[2..];
            else if (input.StartsWith("/./", StringComparison.Ordinal)) input = input[2..];
            else if (input == "/.") input = "/";
            else if (input.StartsWith("/../", StringComparison.Ordinal) || input == "/..")
            {
                input = input.Length == 3 ? "/" : input[3..];
                var last = output.ToString().LastIndexOf('/');
                output.Length = last < 0 ? 0 : last;
            }
            else if (input is "." or "..") input = "";
            else
            {
                // Move the first segment (with its leading "/", if any) to the output.
                var start = input.StartsWith('/') ? 1 : 0;
                var next = input.IndexOf('/', start);
                if (next < 0) next = input.Length;
                output.Append(input, 0, next);
                input = input[next..];
            }
        }
        return output.ToString();
    }
}
