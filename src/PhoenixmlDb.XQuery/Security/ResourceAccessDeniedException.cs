namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Thrown when a resource access is denied by the configured <see cref="ResourcePolicy"/>.
/// </summary>
public sealed class ResourceAccessDeniedException : Exception
{
    public string Uri { get; }
    public ResourceAccessKind RequestedAccess { get; }

    public ResourceAccessDeniedException(string uri, ResourceAccessKind requestedAccess, string reason)
        : base($"Resource policy denied {requestedAccess} access to '{uri}': {reason}")
    {
        Uri = uri;
        RequestedAccess = requestedAccess;
    }
}
