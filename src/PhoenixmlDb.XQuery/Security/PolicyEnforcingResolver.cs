using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Wraps an <see cref="IDocumentResolver"/> and enforces <see cref="ResourcePolicy"/> rules
/// before delegating to the inner resolver or a custom <see cref="IResourceResolver"/>.
/// </summary>
/// <remarks>
/// The inner resolver receives the URI the policy authorised (absolute, with a <c>file:</c>
/// path canonicalised), never the raw argument, so what was checked is what is opened.
/// Hosts may use it for their own resource reads; <see cref="ResourcePolicy.Authorize"/> is
/// the underlying check.
/// </remarks>
public sealed class PolicyEnforcingResolver : IDocumentResolver
{
    private readonly IDocumentResolver? _inner;
    private readonly IResourceResolver? _custom;
    // Documents built from host-supplied content, so a second doc() of one URI is the same node.
    private readonly Dictionary<string, XdmDocument> _hostDocuments = new(StringComparer.Ordinal);
    private readonly ResourcePolicy _policy;

    /// <summary>The policy this resolver enforces.</summary>
    public ResourcePolicy Policy => _policy;
    private int _documentLoadCount;
    private int _textLoadCount;

    /// <summary>Creates a resolver enforcing <paramref name="policy"/> in front of <paramref name="inner"/>.</summary>
    public PolicyEnforcingResolver(IDocumentResolver? inner, ResourcePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _inner = inner;
        _custom = policy.ResourceResolver;
        _policy = policy;
    }

    /// <inheritdoc />
    public XdmDocument? ResolveDocument(string uri) => ResolveDocument(uri, callerBaseUri: null);

    /// <summary>
    /// <see cref="ResolveDocument(string)"/> for a call a module makes: the host's resolver is
    /// told the static base URI of that module.
    /// </summary>
    internal XdmDocument? ResolveDocument(string uri, Uri? callerBaseUri)
    {
        CheckBudget(ref _documentLoadCount, _policy.MaxDocumentLoads, uri, ResourceAccessKind.ReadDocument);

        // Content the host supplies for the document, built here into the store the query
        // navigates. ResolveDocument, the older member, has to return nodes, and a host cannot
        // build those into this store itself.
        if (_custom != null)
        {
            if (_hostDocuments.TryGetValue(uri, out var servedBefore))
                return servedBefore;
            if (_inner is IHostDocumentBuilder builder
                && _custom.ResolveContent(new ResourceRequest(uri, callerBaseUri, ResourceAccessKind.ReadDocument)) is { } content
                && UnderPolicy(() => builder.BuildHostDocument(uri, content)) is { } built)
            {
                _hostDocuments[uri] = built;
                return built;
            }
        }

        // A relative name ("orders", "config.xml") may be a logical name the host's custom
        // resolver serves; offer it there unchecked. Anything the default readers would open —
        // an absolute URI or a path — is authorised first, as before.
        if (_custom != null && (IsRelativeName(uri) || _custom.SuppliesAllContent))
        {
            // A resolver that is the only source of resources decides for itself what it
            // serves; the policy's URI rules describe what the ENGINE may open, and here it
            // opens nothing.
            var named = _custom.ResolveDocument(uri, ResourceAccessKind.ReadDocument);
            if (named != null)
                return named;
            if (_custom.SuppliesAllContent)
                throw new ResourceAccessDeniedException(uri, ResourceAccessKind.ReadDocument,
                    "the host's resource resolver, which is the only source of resources here, did not supply it");
        }

        var authorizedUri = _policy.Authorize(uri, ResourceAccessKind.ReadDocument);
        if (_custom != null && !IsRelativeName(uri))
        {
            var doc = _custom.ResolveDocument(uri, ResourceAccessKind.ReadDocument);
            if (doc != null)
                return doc;
        }
        var authorized = authorizedUri.AbsoluteUri;
        return _inner is XdmDocumentStore store
            ? UnderPolicy(() => store.ResolveDocument(authorized, RedirectCheck(ResourceAccessKind.ReadDocument)))
            : _inner?.ResolveDocument(authorized);
    }

    /// <summary>
    /// Runs a load of the store with this resolver's policy in force for what the loaded
    /// document itself includes (xi:include, where the host has enabled it).
    /// </summary>
    private T UnderPolicy<T>(Func<T> load)
    {
        if (_inner is not XdmDocumentStore store)
            return load();
        var before = store.XIncludePolicy;
        store.XIncludePolicy = _policy;
        try { return load(); }
        finally { store.XIncludePolicy = before; }
    }

    /// <inheritdoc />
    public bool IsDocumentAvailable(string uri) => IsDocumentAvailable(uri, callerBaseUri: null);

    /// <summary><see cref="IsDocumentAvailable(string)"/> for a call a module makes.</summary>
    internal bool IsDocumentAvailable(string uri, Uri? callerBaseUri)
    {
        // The whole request first; a host that answers it is not asked by location alone.
        if (_custom?.IsAvailable(new ResourceRequest(uri, callerBaseUri, ResourceAccessKind.ReadDocument)) is { } answer)
            return answer;
        if (_custom != null && _custom.IsDocumentAvailable(uri))
            return true;
        if (_custom is { SuppliesAllContent: true })
            return false;

        // Check policy without throwing — availability checks shouldn't fail, and a denied
        // resource must not answer whether it exists.
        if (TryAuthorize(uri, ResourceAccessKind.ReadDocument) is not { } authorized)
            return false;

        // A redirect is authorised like the URI itself: an availability check fetches the
        // document, and what it fetched is then what fn:doc serves.
        return _inner is XdmDocumentStore store
            ? store.IsDocumentAvailable(authorized.AbsoluteUri, RedirectCheck(ResourceAccessKind.ReadDocument))
            : _inner?.IsDocumentAvailable(authorized.AbsoluteUri) ?? false;
    }

    /// <inheritdoc />
    public IEnumerable<XdmNode> ResolveCollection(string? uri)
    {
        // A resolver that is the only source of resources decides for itself what it serves,
        // as for a document. What it does not supply is not read by its URI: only a collection
        // the host registered, or the documents it loaded, is left to serve.
        if (_custom is { SuppliesAllContent: true })
        {
            return _custom.ResolveCollection(uri)
                ?? (_inner is XdmDocumentStore loaded
                    ? loaded.ResolveCollection(uri, authorizeRedirect: null, readByName: false)
                    : []);
        }

        string? authorized = null;
        if (uri != null && !(_custom != null && IsRelativeName(uri)))
            authorized = _policy.Authorize(uri, ResourceAccessKind.ReadCollection).AbsoluteUri;

        if (_custom != null)
        {
            var result = _custom.ResolveCollection(uri);
            if (result != null)
                return result;
        }

        // A relative name the custom resolver did not serve reaches the default reader only
        // once authorised.
        if (uri != null && authorized == null)
            authorized = _policy.Authorize(uri, ResourceAccessKind.ReadCollection).AbsoluteUri;
        // A collection that is one document read over HTTP: every redirect is authorised again.
        // Read here, while the policy is in force for what the documents include.
        return _inner is XdmDocumentStore store
            ? UnderPolicy(() => store.ResolveCollection(authorized, RedirectCheck(ResourceAccessKind.ReadCollection), readByName: true).ToList())
            : _inner?.ResolveCollection(authorized) ?? [];
    }

    /// <summary>
    /// Resolves text content, enforcing policy. Used by unparsed-text() functions.
    /// </summary>
    internal string? ResolveText(string uri, string? encoding)
    {
        CheckBudget(ref _textLoadCount, _policy.MaxUnparsedTextLoads, uri, ResourceAccessKind.ReadText);

        // As for documents: a relative name may be a logical name for the custom resolver;
        // anything a default reader could open is authorised first, and a denial throws.
        if (!IsRelativeName(uri) && _custom is not { SuppliesAllContent: true })
            CheckAccess(uri, ResourceAccessKind.ReadText);

        if (_custom != null)
        {
            var text = _custom.ResolveText(uri, encoding);
            if (text != null)
                return text;
        }

        // Fall through to default file-based resolution (handled by caller, which must
        // authorise the URI it actually opens, resolved against its base).
        return null;
    }

    /// <summary>Authorises a text read, returning the URI to open (see <see cref="ResourcePolicy.Authorize"/>).</summary>
    internal Uri AuthorizeText(string uri, Uri? baseUri) => _policy.Authorize(uri, ResourceAccessKind.ReadText, baseUri);

    /// <summary>
    /// Checks text availability without loading.
    /// </summary>
    internal bool IsTextAvailable(string uri) => IsTextAvailable(uri, callerBaseUri: null);

    /// <summary><see cref="IsTextAvailable(string)"/> for a call a module makes.</summary>
    internal bool IsTextAvailable(string uri, Uri? callerBaseUri)
    {
        if (_custom?.IsAvailable(new ResourceRequest(uri, callerBaseUri, ResourceAccessKind.ReadText)) is { } answer)
            return answer;
        if (_custom != null && _custom.IsTextAvailable(uri))
            return true;
        if (_custom is { SuppliesAllContent: true })
            return false;

        return TryAuthorize(uri, ResourceAccessKind.ReadText) is not null; // Caller does actual file existence check
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

    private void CheckAccess(string uriString, ResourceAccessKind access) => _policy.Authorize(uriString, access);

    private Uri? TryAuthorize(string uriString, ResourceAccessKind access) => _policy.TryAuthorize(uriString, access);

    /// <summary>A redirect authoriser for <see cref="HttpDocumentClient"/> under this policy.</summary>
    internal Func<Uri, bool> RedirectCheck(ResourceAccessKind access) => target => _policy.IsAllowed(target, access);

    // Neither an absolute URI nor a rooted path: a name only a custom resolver can give meaning to.
    private static bool IsRelativeName(string uri)
    {
        var text = uri.Trim();
        return text.Length > 0 && !Path.IsPathRooted(text) && !Uri.TryCreate(text, UriKind.Absolute, out _);
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
