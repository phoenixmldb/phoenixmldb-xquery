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
    internal static async ValueTask<string?> ReadUnparsedText(string href, System.Text.Encoding? encoding, Ast.ExecutionContext context)
    {
        if (href.Length > 0)
            ValidateHref(href);
        var resolvedPath = ResolveHref(href, context);
        try
        {
            if (encoding != null)
                return await File.ReadAllTextAsync(resolvedPath, encoding).ConfigureAwait(false);
            return await File.ReadAllTextAsync(resolvedPath).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new XQueryRuntimeException("FOUT1170", $"Cannot read resource '{href}': {ex.Message}");
        }
    }

    /// <summary>Validate href: reject fragment identifiers and invalid URIs.</summary>
    internal static void ValidateHref(string href)
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

    /// <summary>Resolve href against static base URI or as file path.</summary>
    internal static string ResolveHref(string href, Ast.ExecutionContext context)
    {
        // Try as absolute URI first
        if (Uri.TryCreate(href, UriKind.Absolute, out var absUri))
        {
            if (absUri.IsFile)
            {
                if (!File.Exists(absUri.LocalPath))
                    throw new XQueryRuntimeException("FOUT1170", $"Resource not found: '{href}'");
                return absUri.LocalPath;
            }
            // Non-file URIs (http, etc.) — resource not available
            throw new XQueryRuntimeException("FOUT1170", $"Cannot retrieve resource: '{href}'");
        }
        // href is relative — must resolve against static base URI
        var baseUri = context.StaticBaseUri;
        if (baseUri != null && Uri.TryCreate(baseUri, UriKind.Absolute, out var baseUriObj))
        {
            if (Uri.TryCreate(baseUriObj, href, out var resolved) && resolved.IsFile)
            {
                if (!File.Exists(resolved.LocalPath))
                    throw new XQueryRuntimeException("FOUT1170", $"Resource not found: '{href}'");
                return resolved.LocalPath;
            }
        }
        // No base URI or resolution failed — relative URI cannot be resolved
        if (baseUri == null)
            throw new XQueryRuntimeException("FOUT1170", $"Cannot resolve relative URI without a base URI: '{href}'");
        throw new XQueryRuntimeException("FOUT1170", $"Resource not found: '{href}'");
    }
}

/// <summary>fn:unparsed-text($href as xs:string?, $encoding as xs:string) as xs:string?</summary>
public sealed class UnparsedText2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "encoding"), Type = XdmSequenceType.String }];

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var href = arguments[0]?.ToString();
        if (href is null) return null;
        var encodingName = arguments[1]?.ToString() ?? "utf-8";
        System.Text.Encoding encoding;
        try { encoding = System.Text.Encoding.GetEncoding(encodingName); }
        catch (ArgumentException) { throw new XQueryRuntimeException("FOUT1190", $"Unknown encoding: '{encodingName}'"); }
        return await UnparsedTextFunction.ReadUnparsedText(href, encoding, context).ConfigureAwait(false);
    }
}

/// <summary>fn:unparsed-text-available($href as xs:string?) as xs:boolean</summary>
public sealed class UnparsedTextAvailableFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text-available");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var href = arguments[0]?.ToString();
        if (href is null) return ValueTask.FromResult<object?>(false);
        try
        {
            if (href.Length > 0)
                UnparsedTextFunction.ValidateHref(href);
            UnparsedTextFunction.ResolveHref(href, context);
            return ValueTask.FromResult<object?>(true);
        }
        catch (XQueryRuntimeException)
        {
            return ValueTask.FromResult<object?>(false);
        }
    }
}

/// <summary>fn:unparsed-text-available($href as xs:string?, $encoding as xs:string) as xs:boolean</summary>
public sealed class UnparsedTextAvailable2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text-available");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "encoding"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        return new UnparsedTextAvailableFunction().InvokeAsync([arguments[0]], context);
    }
}

/// <summary>fn:unparsed-text-lines($href as xs:string?) as xs:string*</summary>
public sealed class UnparsedTextLinesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text-lines");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString }];

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var text = await new UnparsedTextFunction().InvokeAsync(arguments, context).ConfigureAwait(false);
        if (text is not string s) return Array.Empty<object>();
        return s.Split('\n').Select(l => (object?)l.TrimEnd('\r')).ToArray();
    }
}

/// <summary>fn:unparsed-text-lines($href as xs:string?, $encoding as xs:string) as xs:string*</summary>
public sealed class UnparsedTextLines2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text-lines");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "encoding"), Type = XdmSequenceType.String }];

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var text = await new UnparsedText2Function().InvokeAsync(arguments, context).ConfigureAwait(false);
        if (text is not string s) return Array.Empty<object>();
        return s.Split('\n').Select(l => (object?)l.TrimEnd('\r')).ToArray();
    }
}

/// <summary>fn:analyze-string($input as xs:string?, $pattern as xs:string) as element(fn:analyze-string-result)</summary>
public sealed class AnalyzeStringFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "analyze-string");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        return InvokeWithFlags(arguments[0]?.ToString() ?? "", arguments[1]?.ToString() ?? "", null, context);
    }

    internal static ValueTask<object?> InvokeWithFlags(string input, string pattern, string? flags, Ast.ExecutionContext context)
    {
        var options = System.Text.RegularExpressions.RegexOptions.None;
        if (flags?.Contains('i') == true) options |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        if (flags?.Contains('m') == true) options |= System.Text.RegularExpressions.RegexOptions.Multiline;
        if (flags?.Contains('s') == true) options |= System.Text.RegularExpressions.RegexOptions.Singleline;
        if (flags?.Contains('x') == true) options |= System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace;

        // Build XML result per XQuery spec
        var sb = new System.Text.StringBuilder();
        sb.Append("<fn:analyze-string-result xmlns:fn=\"http://www.w3.org/2005/xpath-functions\">");

        var regex = new System.Text.RegularExpressions.Regex(pattern, options);
        int pos = 0;
        foreach (System.Text.RegularExpressions.Match match in regex.Matches(input))
        {
            if (match.Index > pos)
                sb.Append("<fn:non-match>").Append(System.Security.SecurityElement.Escape(input[pos..match.Index])).Append("</fn:non-match>");
            sb.Append("<fn:match>");
            if (match.Groups.Count > 1)
            {
                int mPos = 0;
                for (int g = 1; g < match.Groups.Count; g++)
                {
                    var group = match.Groups[g];
                    if (!group.Success)
                    {
                        // Non-participating group — emit empty group element
                        sb.Append($"<fn:group nr=\"{g}\"/>");
                        continue;
                    }
                    var groupStart = group.Index - match.Index;
                    if (groupStart > mPos)
                        sb.Append(System.Security.SecurityElement.Escape(match.Value[mPos..groupStart]));
                    sb.Append($"<fn:group nr=\"{g}\">").Append(System.Security.SecurityElement.Escape(group.Value)).Append("</fn:group>");
                    mPos = Math.Max(mPos, groupStart + group.Length);
                }
                if (mPos < match.Length)
                    sb.Append(System.Security.SecurityElement.Escape(match.Value[mPos..]));
            }
            else
            {
                sb.Append(System.Security.SecurityElement.Escape(match.Value));
            }
            sb.Append("</fn:match>");
            pos = match.Index + match.Length;
        }
        if (pos < input.Length)
            sb.Append("<fn:non-match>").Append(System.Security.SecurityElement.Escape(input[pos..])).Append("</fn:non-match>");

        sb.Append("</fn:analyze-string-result>");

        // Parse the result as an XDM element
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(sb.ToString());
            return ValueTask.FromResult<object?>(doc.Root);
        }
        catch
        {
            return ValueTask.FromResult<object?>(sb.ToString());
        }
    }
}

/// <summary>fn:analyze-string($input, $pattern, $flags) as element(fn:analyze-string-result)</summary>
public sealed class AnalyzeString3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "analyze-string");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String },
         new() { Name = new QName(NamespaceId.None, "flags"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        return AnalyzeStringFunction.InvokeWithFlags(
            arguments[0]?.ToString() ?? "", arguments[1]?.ToString() ?? "",
            arguments[2]?.ToString(), context);
    }
}

/// <summary>fn:parse-ietf-date($value as xs:string?) as xs:dateTime?</summary>
public sealed class ParseIetfDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-ietf-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var value = arguments[0]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(value)) return ValueTask.FromResult<object?>(null);

        // Try common IETF date formats (RFC 2822, RFC 850, asctime)
        string[] formats = [
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, d MMM yyyy HH:mm:ss 'GMT'",
            "ddd, d MMM yyyy HH:mm:ss zzz",
            "dd MMM yyyy HH:mm:ss 'GMT'",
            "dd MMM yyyy HH:mm:ss zzz",
            "d MMM yyyy HH:mm:ss 'GMT'",
            "d MMM yyyy HH:mm:ss zzz",
            "ddd MMM dd HH:mm:ss yyyy",
            "ddd MMM d HH:mm:ss yyyy",
            "ddd, dd-MMM-yy HH:mm:ss 'GMT'",
            "ddd, dd-MMM-yyyy HH:mm:ss 'GMT'",
        ];

        // Handle timezone: check for GMT/UT/UTC suffixes and military/US timezones
        var normalizedValue = value;
        TimeSpan? explicitTz = null;
        if (value.EndsWith(" GMT", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(" UT", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(" UTC", StringComparison.OrdinalIgnoreCase))
        {
            explicitTz = TimeSpan.Zero;
            // Strip the timezone suffix for parsing
            var tzIdx = value.LastIndexOf(' ');
            normalizedValue = value[..tzIdx];
        }
        else
        {
            // Check for US timezone abbreviations
            var tzMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["EST"] = -5, ["EDT"] = -4, ["CST"] = -6, ["CDT"] = -5,
                ["MST"] = -7, ["MDT"] = -6, ["PST"] = -8, ["PDT"] = -7
            };
            foreach (var (tz, hours) in tzMap)
            {
                if (value.EndsWith(" " + tz, StringComparison.OrdinalIgnoreCase))
                {
                    explicitTz = TimeSpan.FromHours(hours);
                    normalizedValue = value[..^(tz.Length + 1)];
                    break;
                }
            }
        }

        // Formats without timezone suffix (timezone handled separately)
        string[] noTzFormats = [
            "ddd, dd MMM yyyy HH:mm:ss",
            "ddd, d MMM yyyy HH:mm:ss",
            "dd MMM yyyy HH:mm:ss",
            "d MMM yyyy HH:mm:ss",
            "ddd MMM dd HH:mm:ss yyyy",
            "ddd MMM d HH:mm:ss yyyy",
            "MMM dd HH:mm:ss yyyy",
            "MMM d HH:mm:ss yyyy",
            "MMM-dd HH:mm yyyy",
            "MMM dd HH:mm yyyy",
            "MMM d HH:mm yyyy",
        ];

        // Formats with embedded timezone offset
        string[] withTzFormats = [
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, d MMM yyyy HH:mm:ss zzz",
            "dd MMM yyyy HH:mm:ss zzz",
            "d MMM yyyy HH:mm:ss zzz",
        ];

        if (explicitTz.HasValue)
        {
            // Parse without timezone, then apply explicit timezone
            if (DateTime.TryParseExact(normalizedValue.Trim(), noTzFormats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var dt))
            {
                var dto = new DateTimeOffset(dt, explicitTz.Value);
                return ValueTask.FromResult<object?>(new Xdm.XsDateTime(dto, true));
            }
        }

        // Try formats with embedded offset
        if (DateTimeOffset.TryParseExact(value, withTzFormats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var dto3))
        {
            return ValueTask.FromResult<object?>(new Xdm.XsDateTime(dto3, true));
        }

        // Try general parsing as fallback
        if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var dto2))
        {
            return ValueTask.FromResult<object?>(new Xdm.XsDateTime(dto2, true));
        }

        throw new Execution.XQueryRuntimeException("FORG0010",
            $"Invalid IETF date format: '{value}'");
    }
}
