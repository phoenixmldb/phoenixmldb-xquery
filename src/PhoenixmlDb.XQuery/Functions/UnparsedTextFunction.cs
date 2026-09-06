using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:unparsed-text($href as xs:string?) as xs:string?</summary>
public sealed class UnparsedTextFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString }];

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var href = arguments[0]?.ToString();
        if (href is null) return null;
        return await ReadUnparsedText(href, null, context).ConfigureAwait(false);
    }

    /// <summary>Core implementation shared by 1-arg and 2-arg forms.</summary>
    internal static async ValueTask<string?> ReadUnparsedText(string href, System.Text.Encoding? requestedEncoding, Ast.ExecutionContext context)
    {
        if (href.Length > 0)
            ValidateHref(href);
        var resolvedPath = ResolveHref(href, context);
        try
        {
            // Read raw bytes so we can handle encoding detection, validation, and BOM stripping
            var bytes = await File.ReadAllBytesAsync(resolvedPath).ConfigureAwait(false);
            var encoding = requestedEncoding ?? DetectEncoding(bytes);
            // Use strict decoding (throw on invalid bytes)
            var strictEncoding = System.Text.Encoding.GetEncoding(
                encoding.CodePage,
                new System.Text.EncoderExceptionFallback(),
                new System.Text.DecoderExceptionFallback());
            string text;
            try
            {
                text = strictEncoding.GetString(bytes);
            }
            catch (System.Text.DecoderFallbackException)
            {
                throw new XQueryRuntimeException("FOUT1200",
                    $"The content of resource '{href}' contains octets not valid in encoding '{encoding.WebName}'");
            }
            // Strip BOM if present
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text[1..];
            // Validate: reject non-XML characters (XQuery spec: FOUT1200)
            ValidateXmlCharacters(text, href);
            return text;
        }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new XQueryRuntimeException("FOUT1170", $"Cannot read resource '{href}': {ex.Message}");
        }
    }

    /// <summary>Detect encoding from BOM or default to UTF-8.</summary>
    private static System.Text.Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode; // UTF-16LE
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode; // UTF-16BE
        return System.Text.Encoding.UTF8;
    }

    /// <summary>Validate that the text contains only characters valid in XML 1.0.</summary>
    private static void ValidateXmlCharacters(string text, string href, Ast.ExecutionContext? context = null)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++; // skip low surrogate — supplementary characters are valid
                    continue;
                }
                throw new XQueryRuntimeException("FOUT1200",
                    $"Resource '{href}' contains an unpaired surrogate (U+{(int)c:X4})");
            }
            if (char.IsLowSurrogate(c))
                throw new XQueryRuntimeException("FOUT1200",
                    $"Resource '{href}' contains an unpaired surrogate (U+{(int)c:X4})");
            // XML 1.0 valid: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD]
            if (c == '\t' || c == '\n' || c == '\r')
                continue;
            if (c < 0x20 || (c >= 0xFFFE && c <= 0xFFFF))
                throw new XQueryRuntimeException("FOUT1200",
                    $"Resource '{href}' contains a character not valid in XML (U+{(int)c:X4})");
        }
    }

    /// <summary>Validate href: reject fragment identifiers and invalid URIs.</summary>
    internal static void ValidateHref(string href, Ast.ExecutionContext? context = null)
    {
        // Fragment identifiers are not allowed
        if (href.Contains('#', StringComparison.Ordinal))
            throw new XQueryRuntimeException("FOUT1170", $"URI must not contain a fragment identifier: '{href}'");
        // Check for invalid percent-encoding
        for (int i = 0; i < href.Length; i++)
        {
            if (href[i] == '%')
            {
                if (i + 2 >= href.Length || !IsHexDigit(href[i + 1]) || !IsHexDigit(href[i + 2]))
                    throw new XQueryRuntimeException("FOUT1170", $"Invalid percent-encoding in URI: '{href}'");
            }
        }
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    /// <summary>Resolve href against static base URI or as file path.
    /// Also checks resource mappings to translate http:// URIs to local file paths.</summary>
    internal static string ResolveHref(string href, Ast.ExecutionContext context)
    {
        // Resolve the href to an absolute URI
        string? absoluteUri = null;

        if (Uri.TryCreate(href, UriKind.Absolute, out var absUri))
        {
            absoluteUri = absUri.AbsoluteUri;
        }
        else
        {
            // href is relative — must resolve against static base URI
            var baseUri = context.StaticBaseUri;
            if (baseUri != null && Uri.TryCreate(baseUri, UriKind.Absolute, out var baseUriObj))
            {
                if (Uri.TryCreate(baseUriObj, href, out var resolved))
                    absoluteUri = resolved.AbsoluteUri;
            }
            else if (baseUri == null)
            {
                throw new XQueryRuntimeException("FOUT1170",
                    $"Cannot resolve relative URI without a base URI: '{href}'");
            }
        }

        if (absoluteUri == null)
            throw new XQueryRuntimeException("FOUT1170", $"Resource not found: '{href}'");

        // Check resource mappings (e.g. http:// test URIs → local file paths)
        var mappings = context.ResourceMappings;
        if (mappings != null && mappings.TryGetValue(absoluteUri, out var mappedPath))
        {
            if (!File.Exists(mappedPath))
                throw new XQueryRuntimeException("FOUT1170", $"Resource not found: '{href}'");
            return mappedPath;
        }

        // Try as file URI
        if (Uri.TryCreate(absoluteUri, UriKind.Absolute, out var finalUri) && finalUri.IsFile)
        {
            if (!File.Exists(finalUri.LocalPath))
                throw new XQueryRuntimeException("FOUT1170", $"Resource not found: '{href}'");
            return finalUri.LocalPath;
        }

        // Non-file URI without a resource mapping — cannot retrieve
        throw new XQueryRuntimeException("FOUT1170", $"Resource not found: '{href}'");
    }
}
