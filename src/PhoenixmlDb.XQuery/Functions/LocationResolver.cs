namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Makes the location a function was given absolute, without throwing: what a query writes
/// is not always a URI .NET accepts, and <c>new Uri(base, href)</c> throws for those.
/// </summary>
internal static class LocationResolver
{
    /// <summary>
    /// The location to load for <paramref name="href"/>: itself when it is absolute or there
    /// is no absolute base URI to resolve it against, and otherwise resolved against
    /// <paramref name="staticBaseUri"/>. Null when it cannot be resolved: it is not a URI.
    /// </summary>
    /// <remarks>
    /// <c>file:/abs/path</c>, a file URI with no authority (RFC 8089), is the same location as
    /// <c>file:///abs/path</c>. .NET takes it for neither an absolute URI nor a relative
    /// reference, so every caller that then built a URI from it threw UriFormatException.
    /// </remarks>
    public static string? Absolute(string href, string? staticBaseUri)
    {
        href = WithAuthority(href);
        if (Uri.TryCreate(href, UriKind.Absolute, out _))
            return href;
        if (staticBaseUri == null || !Uri.TryCreate(staticBaseUri, UriKind.Absolute, out var baseUri))
            return href;
        return Uri.TryCreate(baseUri, href, out var resolved) ? resolved.AbsoluteUri : null;
    }

    /// <summary>
    /// Neither an absolute URI nor a rooted path: a relative reference, which names nothing
    /// without a base URI to resolve it against. Only a host can give such a name a meaning, by
    /// supplying, registering or mapping it; the engine does not resolve it against the
    /// process's working directory.
    /// </summary>
    public static bool IsRelativeName(string uri)
    {
        var text = uri.Trim();
        return text.Length > 0 && !Path.IsPathRooted(text) && !Uri.TryCreate(text, UriKind.Absolute, out _);
    }

    /// <summary>The reason given when <paramref name="href"/> is relative and there is no base URI.</summary>
    public static string NoBaseUri(string href) =>
        $"Cannot retrieve '{href}': it is a relative URI and there is no base URI to resolve it against";

    /// <summary><c>file:/abs/path</c> written as <c>file:///abs/path</c>; anything else unchanged.</summary>
    public static string WithAuthority(string href) =>
        href.StartsWith("file:/", StringComparison.OrdinalIgnoreCase) && !href.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? "file://" + href["file:".Length..]
            : href;
}
