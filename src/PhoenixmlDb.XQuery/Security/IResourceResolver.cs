using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Custom resource resolver for plugging in external storage backends (S3, database, HTTP, etc.).
/// Return null from any method to fall through to the default resolver.
/// </summary>
public interface IResourceResolver
{
    XdmDocument? ResolveDocument(string uri, ResourceAccessKind access);
    bool IsDocumentAvailable(string uri);
    string? ResolveText(string uri, string? encoding);
    bool IsTextAvailable(string uri);
    IEnumerable<XdmNode>? ResolveCollection(string? uri);
    TextWriter? OpenResultDocument(string href);
    string? ResolveStylesheetModule(string href, Uri? baseUri);
}
