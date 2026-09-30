namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Defines a scoped access rule for resource URIs.
/// </summary>
public sealed record UriRule
{
    /// <summary>URI scheme to match (e.g. "https", "file", "s3"). Use "*" for any scheme.</summary>
    public required string Scheme { get; init; }

    /// <summary>Optional host pattern. Null matches any host. Supports leading wildcard (e.g. "*.example.com").</summary>
    public string? Host { get; init; }

    /// <summary>
    /// Optional port. Null means the scheme's default port when <see cref="Host"/> is set
    /// (allowing <c>example.com</c> no longer allows <c>example.com:8080</c>), and any port when
    /// it is not. <see cref="AnyPort"/> allows every port on the host.
    /// </summary>
    public int? Port { get; init; }

    /// <summary>A <see cref="Port"/> value that allows every port.</summary>
    public const int AnyPort = -1;

    /// <summary>
    /// Optional path prefix. Null matches any path. Must start with "/" if specified. It matches
    /// whole path segments (<c>/data</c> admits <c>/data/x</c> but not <c>/database</c>), compared
    /// case-sensitively except for <c>file:</c> paths on a case-insensitive file system (Windows,
    /// macOS). A <c>file:</c> prefix is compared with the canonical path, symbolic links resolved.
    /// </summary>
    public string? PathPrefix { get; init; }

    /// <summary>Which resource operations this rule allows.</summary>
    public ResourceAccessKind Access { get; init; }

    public static UriRule AllowFileRead(string? pathPrefix = null) =>
        new() { Scheme = "file", PathPrefix = pathPrefix, Access = ResourceAccessKind.AllRead };

    public static UriRule AllowHttpsRead(string? host = null, string? pathPrefix = null) =>
        new() { Scheme = "https", Host = host, PathPrefix = pathPrefix, Access = ResourceAccessKind.AllRead };

    public static UriRule AllowScheme(string scheme, ResourceAccessKind access = ResourceAccessKind.All) =>
        new() { Scheme = scheme, Access = access };

    /// <summary>
    /// Tests whether this rule matches the given URI and access kind.
    /// </summary>
    internal bool Matches(Uri uri, ResourceAccessKind requestedAccess)
    {
        if ((Access & requestedAccess) == 0)
            return false;

        if (Scheme != "*" && !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Host != null && uri.IsAbsoluteUri)
        {
            if (Host.StartsWith('*'))
            {
                var suffix = Host[1..]; // e.g. ".example.com"
                if (!uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else if (!string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (Host != null && uri.IsAbsoluteUri && Port != AnyPort)
        {
            if (Port is { } port ? uri.Port != port : !uri.IsDefaultPort)
                return false;
        }
        else if (Port is { } onlyPort && onlyPort != AnyPort && uri.Port != onlyPort)
        {
            return false;
        }

        if (PathPrefix != null && uri.IsAbsoluteUri)
        {
            if (!PathMatches(uri, PathPrefix))
                return false;
        }

        return true;
    }

    private static bool PathMatches(Uri uri, string prefix)
    {
        string path;
        if (uri.IsFile)
        {
            // Compare canonical file-system paths: both sides with links resolved and in the
            // platform's form, so neither a link nor ".." nor an escaped character slips past.
            path = uri.LocalPath;
            try
            {
                prefix = ResourcePolicy.CanonicalPath(prefix.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(prefix).LocalPath : prefix);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
            {
                return false;
            }
        }
        else
        {
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }

        var comparison = uri.IsFile && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var trimmed = prefix.Length > 1 ? prefix.TrimEnd('/', Path.DirectorySeparatorChar) : prefix;
        if (!path.StartsWith(trimmed, comparison))
            return false;
        // Whole segments only: the prefix ends the path, or a separator follows it.
        return path.Length == trimmed.Length
            || trimmed.EndsWith('/') || trimmed.EndsWith(Path.DirectorySeparatorChar)
            || path[trimmed.Length] == '/' || path[trimmed.Length] == Path.DirectorySeparatorChar;
    }
}
