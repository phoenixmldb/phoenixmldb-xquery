using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Convenience base class for <see cref="IResourceResolver"/> implementations
/// that only need to handle a subset of resource types.
/// </summary>
public abstract class ResourceResolverBase : IResourceResolver
{
    public virtual XdmDocument? ResolveDocument(string uri, ResourceAccessKind access) => null;
    public virtual bool IsDocumentAvailable(string uri) => false;
    public virtual string? ResolveText(string uri, string? encoding) => null;
    public virtual bool IsTextAvailable(string uri) => false;
    public virtual IEnumerable<XdmNode>? ResolveCollection(string? uri) => null;
    public virtual TextWriter? OpenResultDocument(string href) => null;
    public virtual string? ResolveStylesheetModule(string href, Uri? baseUri) => null;
}
