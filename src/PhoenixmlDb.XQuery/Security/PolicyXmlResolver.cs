using System.Xml;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// An <see cref="XmlResolver"/> for System.Xml readers and schema sets that fetches only what a
/// <see cref="ResourcePolicy"/> allows: external entities, external DTD subsets, and
/// <c>xs:include</c>/<c>xs:import</c> schema locations. <c>file:</c> resources are opened at
/// the canonical path the policy authorised; HTTP fetches re-authorise every redirect.
/// </summary>
public sealed class PolicyXmlResolver : XmlResolver
{
    private readonly ResourcePolicy _policy;
    private readonly ResourceAccessKind _access;
    private readonly ResourceCaller _caller;

    /// <summary>
    /// Creates a resolver checking every fetch against <paramref name="policy"/> for
    /// <paramref name="access"/> (ReadDocument for entities and DTDs, ImportStylesheet for schemas).
    /// </summary>
    public PolicyXmlResolver(ResourcePolicy policy, ResourceAccessKind access = ResourceAccessKind.ReadDocument)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
        _access = access;
    }

    /// <summary>
    /// Creates a resolver for a load a module makes (<c>fn:parse-xml</c> of text with an
    /// external entity): the host's resolver is told who asks.
    /// </summary>
    internal PolicyXmlResolver(ResourcePolicy policy, ResourceAccessKind access, ResourceCaller caller) : this(policy, access)
        => _caller = caller;

    /// <inheritdoc />
    public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
    {
        ArgumentNullException.ThrowIfNull(absoluteUri);
        if (ofObjectToReturn != null && ofObjectToReturn != typeof(Stream) && ofObjectToReturn != typeof(object))
            throw new XmlException($"Unsupported entity type '{ofObjectToReturn}'");
        // The host's own content first; with it, nothing is opened here. System.Xml knows the
        // entity by the URI it asked for, so that is its base whatever the host reports.
        if (ResourceGate.HostContent(_policy, absoluteUri.OriginalString, _caller, _access) is { } supplied)
            return supplied.OpenStream();
        var authorized = _policy.Authorize(absoluteUri.OriginalString, _access);
        if (authorized.IsFile)
            return File.OpenRead(authorized.LocalPath);
        if (authorized.Scheme == Uri.UriSchemeHttp || authorized.Scheme == Uri.UriSchemeHttps)
            return HttpDocumentClient.OpenRead(authorized, target => _policy.IsAllowed(target, _access));
        throw new ResourceAccessDeniedException(absoluteUri.OriginalString, _access,
            $"scheme '{authorized.Scheme}' cannot be fetched");
    }

    /// <inheritdoc />
    public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
    {
        // Resolve as System.Xml does, but a rooted path is a file, never a relative reference.
        if (relativeUri != null && ResourcePolicy.Resolve(relativeUri, baseUri) is { } resolved)
            return resolved;
        return base.ResolveUri(baseUri, relativeUri);
    }
}
