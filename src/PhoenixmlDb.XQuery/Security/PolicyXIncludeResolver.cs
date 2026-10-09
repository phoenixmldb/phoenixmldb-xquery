using System.Xml;
using PhoenixmlDb.Core.Xml;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Resolves what an <c>xi:include</c> names under a resource policy. The document that holds
/// the include was read under the policy, and what it includes is read under it too: from the
/// host's resolver first, and otherwise only a file the policy allows, at its canonical path.
/// Without this the include was opened by Core's own resolver, which knows no policy.
/// </summary>
internal sealed class PolicyXIncludeResolver(ResourcePolicy policy, XIncludeOptions options) : IXmlResourceResolver
{
    private readonly IXmlResourceResolver _inner = options.Resolver
        ?? new LocalFileResourceResolver { AllowRemote = false, MaxResourceBytes = options.MaxResourceBytes };

    /// <summary><paramref name="options"/> with its includes resolved under <paramref name="policy"/>.</summary>
    internal static XIncludeOptions Guard(XIncludeOptions options, ResourcePolicy policy) => new()
    {
        Enabled = options.Enabled,
        AllowRemote = options.AllowRemote,
        MaxIncludeDepth = options.MaxIncludeDepth,
        MaxExpansionDepth = options.MaxExpansionDepth,
        MaxExpandedNodes = options.MaxExpandedNodes,
        MaxResourceBytes = options.MaxResourceBytes,
        MaxXPathEvalMilliseconds = options.MaxXPathEvalMilliseconds,
        Resolver = options.Resolver is PolicyXIncludeResolver ? options.Resolver : new PolicyXIncludeResolver(policy, options),
    };

    public XmlReader ResolveXml(Uri absolute)
    {
        ArgumentNullException.ThrowIfNull(absolute);
        if (ResourceGate.HostContent(policy, absolute.AbsoluteUri, null, ResourceAccessKind.ReadDocument) is { } supplied)
        {
            // As Core's resolver reads an included file: no document type declaration, and the
            // reader itself dereferences nothing.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = true };
            return XmlReader.Create(new MemoryStream(supplied.ReadBytes().Bytes), settings, absolute.AbsoluteUri);
        }
        return _inner.ResolveXml(Authorized(absolute, ResourceAccessKind.ReadDocument));
    }

    public string ResolveText(Uri absolute, string? encoding, string? accept, string? acceptLanguage)
    {
        ArgumentNullException.ThrowIfNull(absolute);
        if (ResourceGate.HostContent(policy, absolute.AbsoluteUri, null, ResourceAccessKind.ReadText) is { } supplied)
            return supplied.ReadText();
        return _inner.ResolveText(Authorized(absolute, ResourceAccessKind.ReadText), encoding, accept, acceptLanguage);
    }

    // Only a file: a remote include would follow redirects the policy never sees.
    private Uri Authorized(Uri absolute, ResourceAccessKind access)
    {
        var authorized = policy.Authorize(absolute.AbsoluteUri, access);
        return authorized.IsFile
            ? authorized
            : throw new ResourceAccessDeniedException(absolute.AbsoluteUri, access,
                "under a resource policy an xi:include reads a file or what the host's resolver supplies");
    }
}
