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

    /// <summary>
    /// Supplies the content of a resource the engine is about to load, so the engine opens
    /// nothing itself: an XQuery library module, a schema document (including those another
    /// schema includes, imports or redefines), a JSON or text resource, a DTD or external entity,
    /// a stylesheet or source document named to fn:transform. Return null when this resolver
    /// does not supply the resource; what happens then is decided by
    /// <see cref="SuppliesAllContent"/>.
    /// </summary>
    /// <remarks>
    /// Without this, the engine authorises a location against the policy and then opens it by
    /// name. Those are two steps, and a file replaced between them (a regular file swapped for a
    /// link out of the allowed root) is read. A host that hands over the content it has itself
    /// verified has no such window. Content supplied here is the host's own decision and is not
    /// checked against the policy's URI rules.
    /// </remarks>
    ResourceContent? ResolveContent(ResourceRequest request) => null;

    /// <summary>
    /// When true, this resolver is the ONLY source of resources: a load it does not supply
    /// fails, and the engine never falls back to opening a file or URL itself. That covers
    /// <see cref="ResolveContent"/> and the older members alike (<see cref="ResolveDocument"/>,
    /// <see cref="ResolveText"/>, <see cref="ResolveStylesheetModule"/>). The default, false,
    /// keeps the fall-through those members have always had.
    /// </summary>
    bool SuppliesAllContent => false;
}

/// <summary>
/// A document store that can build a document from content a host supplied, in the store the
/// query or transformation navigates. A document built anywhere else is not navigable there:
/// its string value is right and its descendants are not found.
/// </summary>
internal interface IHostDocumentBuilder
{
    XdmDocument? BuildHostDocument(string uri, ResourceContent content);
}

/// <summary>What the engine is about to load, as put to <see cref="IResourceResolver.ResolveContent"/>.</summary>
/// <param name="Location">The location as written in the query, stylesheet or document.</param>
/// <param name="BaseUri">The base URI a relative <paramref name="Location"/> is relative to, when known.</param>
/// <param name="Access">Why it is being loaded: a document, text, or an import (module, schema, stylesheet).</param>
public readonly record struct ResourceRequest(string Location, Uri? BaseUri, ResourceAccessKind Access);

/// <summary>
/// A resource's content as supplied by the host, with the base URI it is to be known by:
/// relative references inside it resolve against that URI and come back to the resolver.
/// </summary>
public sealed class ResourceContent
{
    private readonly string? _text;
    private readonly Stream? _stream;

    /// <summary>Content already decoded to text.</summary>
    public ResourceContent(string text, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(baseUri);
        _text = text;
        BaseUri = baseUri;
    }

    /// <summary>Content as bytes; the engine decodes it as it would the file. The engine disposes the stream.</summary>
    public ResourceContent(Stream stream, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(baseUri);
        _stream = stream;
        BaseUri = baseUri;
    }

    /// <summary>The URI this content is known by.</summary>
    public Uri BaseUri { get; }

    /// <summary>The content as a stream of bytes (UTF-8 when it was supplied as text).</summary>
    internal Stream OpenStream() =>
        _stream ?? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_text!), writable: false);

    /// <summary>The content as bytes, with the encoding they are in when that is already known.</summary>
    internal (byte[] Bytes, System.Text.Encoding? KnownEncoding) ReadBytes()
    {
        if (_text != null)
            return (System.Text.Encoding.UTF8.GetBytes(_text), System.Text.Encoding.UTF8);
        using var stream = _stream!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return (buffer.ToArray(), null);
    }

    /// <summary>The content as text; bytes are decoded as UTF-8 unless a byte-order mark says otherwise.</summary>
    internal string ReadText()
    {
        if (_text != null)
            return _text;
        using var stream = _stream!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
