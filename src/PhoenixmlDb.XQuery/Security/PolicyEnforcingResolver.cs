using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Wraps an <see cref="IDocumentResolver"/> and enforces <see cref="ResourcePolicy"/> rules
/// before delegating to the inner resolver or a custom <see cref="IResourceResolver"/>.
/// </summary>
internal sealed class PolicyEnforcingResolver : IDocumentResolver
{
    private readonly IDocumentResolver? _inner;
    private readonly IResourceResolver? _custom;
    private readonly ResourcePolicy _policy;
    private int _documentLoadCount;
    private int _textLoadCount;

    public PolicyEnforcingResolver(IDocumentResolver? inner, ResourcePolicy policy)
    {
        _inner = inner;
        _custom = policy.ResourceResolver;
        _policy = policy;
    }

    public XdmDocument? ResolveDocument(string uri)
    {
        CheckBudget(ref _documentLoadCount, _policy.MaxDocumentLoads, uri, ResourceAccessKind.ReadDocument);
        CheckAccess(uri, ResourceAccessKind.ReadDocument);

        // Try custom resolver first
        if (_custom != null)
        {
            var doc = _custom.ResolveDocument(uri, ResourceAccessKind.ReadDocument);
            if (doc != null)
                return doc;
        }

        return _inner?.ResolveDocument(uri);
    }

    public bool IsDocumentAvailable(string uri)
    {
        if (_custom != null && _custom.IsDocumentAvailable(uri))
            return true;

        // Check policy without throwing — availability checks shouldn't fail
        if (!TryCheckAccess(uri, ResourceAccessKind.ReadDocument))
            return false;

        return _inner?.IsDocumentAvailable(uri) ?? false;
    }

    public IEnumerable<XdmNode> ResolveCollection(string? uri)
    {
        if (uri != null)
            CheckAccess(uri, ResourceAccessKind.ReadCollection);

        if (_custom != null)
        {
            var result = _custom.ResolveCollection(uri);
            if (result != null)
                return result;
        }

        return _inner?.ResolveCollection(uri) ?? [];
    }

    /// <summary>
    /// Resolves text content, enforcing policy. Used by unparsed-text() functions.
    /// </summary>
    internal string? ResolveText(string uri, string? encoding)
    {
        CheckBudget(ref _textLoadCount, _policy.MaxUnparsedTextLoads, uri, ResourceAccessKind.ReadText);
        CheckAccess(uri, ResourceAccessKind.ReadText);

        if (_custom != null)
        {
            var text = _custom.ResolveText(uri, encoding);
            if (text != null)
                return text;
        }

        // Fall through to default file-based resolution (handled by caller)
        return null;
    }

    /// <summary>
    /// Checks text availability without loading.
    /// </summary>
    internal bool IsTextAvailable(string uri)
    {
        if (_custom != null && _custom.IsTextAvailable(uri))
            return true;

        if (!TryCheckAccess(uri, ResourceAccessKind.ReadText))
            return false;

        return true; // Caller does actual file existence check
    }

    /// <summary>
    /// Checks whether a write operation is allowed for the given href.
    /// </summary>
    internal void CheckWriteAccess(string href)
    {
        CheckAccess(href, ResourceAccessKind.WriteDocument);
    }

    /// <summary>
    /// Opens a result document writer via the custom resolver, if available.
    /// </summary>
    internal TextWriter? OpenResultDocument(string href)
    {
        CheckWriteAccess(href);
        return _custom?.OpenResultDocument(href);
    }

    /// <summary>
    /// Resolves a stylesheet module via the custom resolver, if available.
    /// </summary>
    internal string? ResolveStylesheetModule(string href, Uri? baseUri)
    {
        CheckAccess(href, ResourceAccessKind.ImportStylesheet);
        return _custom?.ResolveStylesheetModule(href, baseUri);
    }

    private void CheckAccess(string uriString, ResourceAccessKind access)
    {
        if (!Uri.TryCreate(uriString, UriKind.RelativeOrAbsolute, out var uri))
            throw new ResourceAccessDeniedException(uriString, access, "invalid URI");

        // For relative URIs, we can't check scheme/host — allow them through
        // (the underlying resolver will resolve against the base URI)
        if (!uri.IsAbsoluteUri)
            return;

        if (!_policy.IsAllowed(uri, access))
            throw new ResourceAccessDeniedException(uriString, access,
                $"scheme '{uri.Scheme}' is not allowed by the resource policy");
    }

    private bool TryCheckAccess(string uriString, ResourceAccessKind access)
    {
        if (!Uri.TryCreate(uriString, UriKind.RelativeOrAbsolute, out var uri))
            return false;

        if (!uri.IsAbsoluteUri)
            return true;

        return _policy.IsAllowed(uri, access);
    }

    private static void CheckBudget(ref int counter, int limit, string uri, ResourceAccessKind access)
    {
        if (limit <= 0)
            return;

        if (Interlocked.Increment(ref counter) > limit)
            throw new ResourceAccessDeniedException(uri, access,
                $"resource budget exceeded (limit: {limit})");
    }
}
