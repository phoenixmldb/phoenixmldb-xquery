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
        var authorized = Authorized(absolute, ResourceAccessKind.ReadDocument);
        if (options.Resolver is not null)
            return _inner.ResolveXml(authorized);
        // Core's resolver would open the path again by name. The file is opened here and judged
        // by the file actually open, then read as Core reads an included file.
        var stream = AuthorizedFile.OpenRead(policy, authorized, ResourceAccessKind.ReadDocument, absolute.AbsoluteUri);
        var fileSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false, CloseInput = true };
        if (options.MaxResourceBytes > 0)
            fileSettings.MaxCharactersInDocument = options.MaxResourceBytes;
        try
        {
            return XmlReader.Create(stream, fileSettings);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public string ResolveText(Uri absolute, string? encoding, string? accept, string? acceptLanguage)
    {
        ArgumentNullException.ThrowIfNull(absolute);
        if (ResourceGate.HostContent(policy, absolute.AbsoluteUri, null, ResourceAccessKind.ReadText) is { } supplied)
            return supplied.ReadText();
        var authorized = Authorized(absolute, ResourceAccessKind.ReadText);
        if (options.Resolver is not null)
            return _inner.ResolveText(authorized, encoding, accept, acceptLanguage);
        return ReadText(authorized, absolute, encoding);
    }

    // As Core's resolver reads a local text include (explicit encoding, else byte order mark,
    // else UTF-8; a failure is a resource error the include's fallback can recover from), with
    // the file opened once and judged by the file actually open.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "As Core's resolver: any failure to read is a resource error the include's fallback recovers from.")]
    private string ReadText(Uri authorized, Uri absolute, string? encoding)
    {
        System.Text.Encoding? enc = null;
        if (!string.IsNullOrEmpty(encoding))
        {
            try { enc = System.Text.Encoding.GetEncoding(encoding); }
            catch (ArgumentException ex)
            {
                throw new XIncludeException(XIncludeErrorKind.ResourceError, isFatal: false,
                    $"Unknown parse=text encoding '{encoding}'.", ex);
            }
        }
        byte[] bytes;
        try
        {
            using var stream = AuthorizedFile.OpenRead(policy, authorized, ResourceAccessKind.ReadText, absolute.AbsoluteUri);
            if (options.MaxResourceBytes > 0 && stream.Length > options.MaxResourceBytes)
                throw new XIncludeException(XIncludeErrorKind.ResourceError, isFatal: false,
                    $"resource '{absolute}' exceeds MaxResourceBytes ({options.MaxResourceBytes}).");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is not XIncludeException and not ResourceAccessDeniedException)
        {
            throw new XIncludeException(XIncludeErrorKind.ResourceError, isFatal: false,
                $"Could not read text resource '{absolute}': {ex.Message}", ex);
        }
        var preamble = enc?.GetPreamble() ?? [];
        if (enc != null)
            return preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble)
                ? enc.GetString(bytes, preamble.Length, bytes.Length - preamble.Length)
                : enc.GetString(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            return new System.Text.UTF32Encoding(bigEndian: false, byteOrderMark: false).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return new System.Text.UTF32Encoding(bigEndian: true, byteOrderMark: false).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return System.Text.Encoding.UTF8.GetString(bytes);
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
