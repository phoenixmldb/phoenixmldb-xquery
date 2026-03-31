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

    /// <summary>Optional path prefix. Null matches any path. Must start with "/" if specified.</summary>
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

        if (PathPrefix != null && uri.IsAbsoluteUri)
        {
            if (!uri.AbsolutePath.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
