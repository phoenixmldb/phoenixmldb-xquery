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
    private static void ValidateXmlCharacters(string text, string href)
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

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        RequireStringArgument(arguments[0], "href");
        var href = arguments[0]?.ToString();
        if (href is null) return false;
        try
        {
            // Per spec: returns true iff a call to unparsed-text with the same args would succeed
            await UnparsedTextFunction.ReadUnparsedText(href, null, context).ConfigureAwait(false);
            return true;
        }
        catch (XQueryRuntimeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that a function argument is string-compatible (xs:string, xs:anyURI,
    /// xs:untypedAtomic, or null). Raises XPTY0004 for non-string atomic types like
    /// xs:integer, xs:boolean, etc.
    /// </summary>
    internal static void RequireStringArgument(object? arg, string paramName, bool allowEmpty = true)
    {
        if (arg is null && !allowEmpty)
            throw new XQueryRuntimeException("XPTY0004",
                $"Parameter ${paramName} expects xs:string, got empty sequence");
        if (arg is null or string or Xdm.XsUntypedAtomic or Xdm.XsAnyUri)
            return;
        throw new XQueryRuntimeException("XPTY0004",
            $"Parameter ${paramName} expects xs:string, got {arg.GetType().Name}");
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

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        UnparsedTextAvailableFunction.RequireStringArgument(arguments[0], "href");
        UnparsedTextAvailableFunction.RequireStringArgument(arguments[1], "encoding", allowEmpty: false);
        var href = arguments[0]?.ToString();
        if (href is null) return false;
        var encodingName = arguments[1]?.ToString() ?? "utf-8";
        System.Text.Encoding encoding;
        try { encoding = System.Text.Encoding.GetEncoding(encodingName); }
        catch (ArgumentException) { return false; }
        try
        {
            await UnparsedTextFunction.ReadUnparsedText(href, encoding, context).ConfigureAwait(false);
            return true;
        }
        catch (XQueryRuntimeException)
        {
            return false;
        }
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
        return SplitLines(s);
    }

    /// <summary>
    /// Split text into lines per XQuery spec. Line endings per XML 1.0 Section 2.11:
    /// #xD#xA (CRLF), #xD (CR alone), #xA (LF).
    /// The trailing empty string after the last line ending is not included.
    /// </summary>
    internal static object?[] SplitLines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                lines.Add(text[start..i]);
                // CR followed by LF is a single line ending
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                start = i + 1;
            }
            else if (c == '\n')
            {
                lines.Add(text[start..i]);
                start = i + 1;
            }
        }
        // Add the remaining text after the last line ending
        var trailing = text[start..];
        if (trailing.Length > 0)
            lines.Add(trailing);
        return lines.Select(l => (object?)l).ToArray();
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
        return UnparsedTextLinesFunction.SplitLines(s);
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

    /// <summary>
    /// Determines the parent group number for each capturing group in the regex pattern.
    /// Returns an array where parentGroup[g] is the parent capturing group of group g (1-based),
    /// or 0 if the group is at the top level.
    /// </summary>
    private static int[] BuildGroupParentMap(string pattern)
    {
        int groupCount = XQueryRegexHelper.CountCapturingGroups(pattern);
        var parentGroup = new int[groupCount + 1]; // 1-based indexing
        var groupStack = new Stack<int>(); // stack of capturing group numbers; 0 = non-capturing
        bool inCharClass = false;

        int currentGroup = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length) { i++; continue; }
            if (inCharClass) { if (c == ']') inCharClass = false; continue; }
            if (c == '[') { inCharClass = true; continue; }
            if (c == '(')
            {
                bool isNonCapturing = i + 1 < pattern.Length && pattern[i + 1] == '?';
                if (isNonCapturing)
                {
                    groupStack.Push(0); // sentinel
                }
                else
                {
                    currentGroup++;
                    // Find nearest capturing ancestor on the stack
                    int parent = 0;
                    foreach (var s in groupStack)
                    {
                        if (s > 0) { parent = s; break; }
                    }
                    parentGroup[currentGroup] = parent;
                    groupStack.Push(currentGroup);
                }
            }
            else if (c == ')' && groupStack.Count > 0)
            {
                groupStack.Pop();
            }
        }
        return parentGroup;
    }

    /// <summary>
    /// Emits the XML content for a match element, with properly nested groups.
    /// Groups are nested based on the regex structure (child groups inside parent groups).
    /// Non-participating groups (from alternation) are omitted entirely.
    /// </summary>
    private static void EmitMatchContent(
        System.Text.StringBuilder sb,
        System.Text.RegularExpressions.Match match,
        int[] parentGroup,
        int groupCount)
    {
        if (groupCount == 0)
        {
            sb.Append(System.Security.SecurityElement.Escape(match.Value));
            return;
        }

        // Build children lists: childGroups[p] = list of child group numbers of parent p
        // Parent 0 means "directly inside <fn:match>"
        var childGroups = new List<int>[groupCount + 1];
        for (int i = 0; i <= groupCount; i++)
            childGroups[i] = new List<int>();
        for (int g = 1; g <= groupCount; g++)
            childGroups[parentGroup[g]].Add(g);

        // Recursively emit group content for a given parent context
        EmitGroupChildren(sb, match, parentGroup, childGroups, 0, match.Index, match.Index + match.Length);
    }

    /// <summary>
    /// Emits group content within a parent span [spanStart, spanEnd) (absolute positions in input).
    /// parentGroupNr=0 means the fn:match level.
    /// </summary>
    private static void EmitGroupChildren(
        System.Text.StringBuilder sb,
        System.Text.RegularExpressions.Match match,
        int[] parentGroup,
        List<int>[] childGroups,
        int parentGroupNr,
        int spanStart,
        int spanEnd)
    {
        // Collect participating child groups, sorted by position
        var children = new List<(int groupNr, int start, int end)>();
        foreach (int g in childGroups[parentGroupNr])
        {
            var grp = match.Groups[g];
            if (!grp.Success) continue; // Non-participating groups omitted
            children.Add((g, grp.Index, grp.Index + grp.Length));
        }
        children.Sort((a, b) => a.start.CompareTo(b.start));

        int cursor = spanStart;
        foreach (var (groupNr, start, end) in children)
        {
            // Emit text before this group
            if (start > cursor)
                sb.Append(System.Security.SecurityElement.Escape(
                    match.Value[(cursor - match.Index)..(start - match.Index)]));

            sb.Append($"<fn:group nr=\"{groupNr}\">");
            // Recursively emit children of this group
            EmitGroupChildren(sb, match, parentGroup, childGroups, groupNr, start, end);
            sb.Append("</fn:group>");
            cursor = end;
        }
        // Emit trailing text after last child group
        if (cursor < spanEnd)
            sb.Append(System.Security.SecurityElement.Escape(
                match.Value[(cursor - match.Index)..(spanEnd - match.Index)]));
    }

    internal static ValueTask<object?> InvokeWithFlags(string input, string pattern, string? flags, Ast.ExecutionContext context)
    {
        // Validate and parse flags using shared helper
        var options = System.Text.RegularExpressions.RegexOptions.None;
        bool isLiteral = false;
        if (flags != null)
        {
            options = XQueryRegexHelper.ParseFlags(flags);
            isLiteral = flags.Contains('q');
        }

        // XPath 'x' flag: strip whitespace from pattern before any processing
        if (!isLiteral && flags?.Contains('x') == true)
            pattern = XQueryRegexHelper.StripXModeWhitespace(pattern);

        // Apply 'q' flag: escape pattern so it's treated as a literal string
        string effectivePattern = isLiteral
            ? System.Text.RegularExpressions.Regex.Escape(pattern)
            : pattern;

        // Apply XPath-to-.NET regex transformations (same as fn:matches, fn:replace, fn:tokenize)
        if (!isLiteral)
        {
            XQueryRegexHelper.ValidateXsdRegex(effectivePattern);
            effectivePattern = XQueryRegexHelper.ConvertXPathPatternToNet(effectivePattern);
            effectivePattern = XQueryRegexHelper.ConvertXsdEscapesToNet(effectivePattern);
        }
        bool isSingleLine = flags?.Contains('s') == true;
        effectivePattern = XQueryRegexHelper.FixDotForSurrogatePairs(effectivePattern, isSingleLine);

        var regex = new System.Text.RegularExpressions.Regex(effectivePattern, options);

        // FORX0003: pattern must not match zero-length string
        if (regex.IsMatch(""))
            throw new XQueryException("FORX0003",
                "The supplied regular expression matches a zero-length string");

        // Determine group nesting structure from the original pattern
        int groupCount = isLiteral ? 0 : XQueryRegexHelper.CountCapturingGroups(pattern);
        int[] parentGroup = groupCount > 0 ? BuildGroupParentMap(pattern) : Array.Empty<int>();

        // Build XML result per XQuery spec
        var sb = new System.Text.StringBuilder();
        sb.Append("<fn:analyze-string-result xmlns:fn=\"http://www.w3.org/2005/xpath-functions\">");

        int pos = 0;
        foreach (System.Text.RegularExpressions.Match match in regex.Matches(input))
        {
            if (match.Index > pos)
                sb.Append("<fn:non-match>").Append(System.Security.SecurityElement.Escape(input[pos..match.Index])).Append("</fn:non-match>");
            sb.Append("<fn:match>");
            EmitMatchContent(sb, match, parentGroup, groupCount);
            sb.Append("</fn:match>");
            pos = match.Index + match.Length;
        }
        if (pos < input.Length)
            sb.Append("<fn:non-match>").Append(System.Security.SecurityElement.Escape(input[pos..])).Append("</fn:non-match>");

        sb.Append("</fn:analyze-string-result>");

        // Parse the result and convert to proper XDM nodes for XPath navigation
        var xmlDoc = new System.Xml.XmlDocument();
        xmlDoc.LoadXml(sb.ToString());
        if (context.NodeStore is INodeBuilder builder)
        {
            var xdmDoc = ParseXmlFunction.ConvertToXdm(xmlDoc, builder, documentUri: null);
            xdmDoc.DocumentUri = null;
            xdmDoc.BaseUri = context.StaticBaseUri;
            // Return the document element (the analyze-string-result element)
            if (xdmDoc.DocumentElement is { } docElemId)
                return ValueTask.FromResult<object?>(builder.GetNode(docElemId));
            return ValueTask.FromResult<object?>(xdmDoc);
        }
        // Fallback
        return ValueTask.FromResult<object?>(xmlDoc.DocumentElement);
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

    private static readonly Dictionary<string, int> MonthMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jan"] = 1, ["Feb"] = 2, ["Mar"] = 3, ["Apr"] = 4, ["May"] = 5, ["Jun"] = 6,
        ["Jul"] = 7, ["Aug"] = 8, ["Sep"] = 9, ["Oct"] = 10, ["Nov"] = 11, ["Dec"] = 12
    };

    private static readonly Dictionary<string, int> TzNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMT"] = 0, ["UT"] = 0, ["UTC"] = 0,
        ["EST"] = -5, ["EDT"] = -4, ["CST"] = -6, ["CDT"] = -5,
        ["MST"] = -7, ["MDT"] = -6, ["PST"] = -8, ["PDT"] = -7
    };

    private static readonly HashSet<string> DayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun",
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    };

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var value = arguments[0]?.ToString()?.Trim();
        if (value is null) return ValueTask.FromResult<object?>(null);
        if (value.Length == 0)
            throw new Execution.XQueryRuntimeException("FORG0010", "Empty string is not a valid IETF date");

        try
        {
            var result = ParseIetfDate(value);
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException ex)
        {
            throw new Execution.XQueryRuntimeException("FORG0010",
                $"Invalid IETF date format: '{value}' — {ex.Message}");
        }
    }

    private static Xdm.XsDateTime ParseIetfDate(string input)
    {
        // Pre-validate: reject letter-digit or digit-letter transitions without separator
        // Exception: timezone glued to time (handled by tokenizer) like "01GMT" or "36EST"
        ValidateIetfSeparators(input);

        var tokens = Tokenize(input);
        int pos = 0;

        // Skip optional day name (possibly followed by comma)
        if (pos < tokens.Count && DayNames.Contains(tokens[pos]))
        {
            pos++;
            if (pos < tokens.Count && tokens[pos] == ",") pos++;
        }

        int day, month, year;
        int hour, minute;
        double second = 0;
        TimeSpan? tz = null;

        // Determine format: starts with number (day) or month name
        if (pos < tokens.Count && int.TryParse(tokens[pos], out day))
        {
            // Day must be 1-2 digits
            if (tokens[pos].Length > 2)
                throw new FormatException($"Day must be 1-2 digits: '{tokens[pos]}'");
            // Format: day ["-"] month ["-"] year time [tz]
            pos++;
            SkipSep(tokens, ref pos);
            month = ParseMonth(tokens, ref pos);
            SkipSep(tokens, ref pos);
            year = ParseYear(tokens, ref pos);
            ParseTime(tokens, ref pos, out hour, out minute, out second);
            tz = ParseTimezone(tokens, ref pos);
        }
        else if (pos < tokens.Count && tokens[pos].Length == 3 && MonthMap.ContainsKey(tokens[pos]))
        {
            // Format: month ["-"] day time [tz] year  OR  month ["-"] day ["-"] year time [tz]
            month = ParseMonth(tokens, ref pos);
            SkipSep(tokens, ref pos);
            if (pos >= tokens.Count || !int.TryParse(tokens[pos], out day))
                throw new FormatException("Expected day number");
            if (tokens[pos].Length > 2)
                throw new FormatException($"Day must be 1-2 digits: '{tokens[pos]}'");
            pos++;
            SkipSep(tokens, ref pos);

            // Peek: is the next token a time (contains ':') or a year?
            if (pos < tokens.Count && tokens[pos].Contains(':', StringComparison.Ordinal))
            {
                // month day time [tz] year format
                ParseTime(tokens, ref pos, out hour, out minute, out second);
                tz = ParseTimezone(tokens, ref pos);
                // Year comes after timezone
                if (pos < tokens.Count && int.TryParse(tokens[pos], out year))
                {
                    // Year must be 2 or 4+ digits
                    if (tokens[pos].Length == 1 || tokens[pos].Length == 3)
                        throw new FormatException($"Year must be 2 or 4+ digits: '{tokens[pos]}'");
                    pos++;
                }
                else
                    throw new FormatException("Expected year");
            }
            else
            {
                year = ParseYear(tokens, ref pos);
                ParseTime(tokens, ref pos, out hour, out minute, out second);
                tz = ParseTimezone(tokens, ref pos);
            }
        }
        else
        {
            throw new FormatException("Expected day number or month name");
        }

        // Check for unconsumed tokens (except comments) — indicates parse error
        SkipComment(tokens, ref pos);
        if (pos < tokens.Count)
        {
            // Check if remaining token is an unknown timezone name
            var remaining = tokens[pos];
            if (remaining.Length > 0 && char.IsLetter(remaining[0]))
                throw new FormatException($"Unknown timezone or unexpected token: '{remaining}'");
        }

        // Normalize 2-digit year: per XPath spec, always add 1900
        if (year < 100)
            year += 1900;

        // Default timezone is UTC (per XPath spec: "If no timezone is present, UTC is assumed")
        if (!tz.HasValue)
            tz = TimeSpan.Zero;

        int sec = (int)second;
        // Round fractional seconds to milliseconds
        int ms = (int)Math.Round((second - sec) * 1000);

        // Handle hour=24 (midnight of next day): 24:00:00 → next day 00:00:00
        if (hour == 24 && minute == 0 && sec == 0 && ms == 0)
        {
            var dto = new DateTimeOffset(year, month, day, 0, 0, 0, 0, tz.Value).AddDays(1);
            return new Xdm.XsDateTime(dto, true);
        }

        var result = new DateTimeOffset(year, month, day, hour, minute, sec, ms, tz.Value);
        return new Xdm.XsDateTime(result, true);
    }

    /// <summary>
    /// Tokenize IETF date string. Handles tricky cases where timezone is glued to time
    /// (e.g., "19:36:01GMT", "19:36+0500", "14:36:01-05:00", "14:36:01EST").
    /// </summary>
    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i])) { i++; continue; }
            if (input[i] == ',') { tokens.Add(","); i++; continue; }
            if (input[i] == '(') { tokens.Add("("); i++; continue; }
            if (input[i] == ')') { tokens.Add(")"); i++; continue; }

            // Handle '+' as start of timezone offset
            if (input[i] == '+')
            {
                int start = i; i++;
                while (i < input.Length && (char.IsDigit(input[i]) || input[i] == ':')) i++;
                tokens.Add(input[start..i]);
                continue;
            }

            // '-' is always tokenized as a separator; negative tz offsets handled in ParseTimezone
            if (input[i] == '-')
            {
                tokens.Add("-");
                i++;
                continue;
            }

            // Collect contiguous word (letters) or number (digits, colons, dots)
            int s = i;
            if (char.IsLetter(input[i]))
            {
                while (i < input.Length && char.IsLetter(input[i])) i++;
                tokens.Add(input[s..i]);
            }
            else if (char.IsDigit(input[i]))
            {
                // Collect digits, colons (time), and dots (fractional seconds)
                while (i < input.Length && (char.IsDigit(input[i]) || input[i] == ':' || input[i] == '.')) i++;
                var numTok = input[s..i];
                // Check if letters are glued to the end (e.g., "01GMT", "36EST")
                if (i < input.Length && char.IsLetter(input[i]))
                {
                    int letterStart = i;
                    while (i < input.Length && char.IsLetter(input[i])) i++;
                    var suffix = input[letterStart..i];
                    if (TzNameMap.ContainsKey(suffix))
                    {
                        // Split: number part + timezone name
                        tokens.Add(numTok);
                        tokens.Add(suffix);
                        continue;
                    }
                    // Not a known tz name — treat as single token
                    tokens.Add(numTok + suffix);
                    continue;
                }
                // Check if + or - is glued (e.g., "01+05", "01-05:00")
                if (i < input.Length && (input[i] == '+' || input[i] == '-'))
                {
                    tokens.Add(numTok);
                    // Don't consume the +/- — let the next iteration handle it
                    continue;
                }
                tokens.Add(numTok);
            }
            else
            {
                // Skip unknown characters
                i++;
            }
        }
        return tokens;
    }

    /// <summary>Validate IETF date separators: no letter→digit or comma→non-whitespace transitions.</summary>
    private static void ValidateIetfSeparators(string input)
    {
        for (int i = 0; i < input.Length - 1; i++)
        {
            // After comma, must have whitespace
            if (input[i] == ',' && !char.IsWhiteSpace(input[i + 1]))
                throw new FormatException("Space required after comma");
            // Letter followed directly by digit is invalid (e.g., "Aug20", "GMT2014")
            if (char.IsLetter(input[i]) && char.IsDigit(input[i + 1]))
                throw new FormatException("Separator required between text and number");
        }
    }

    private static void SkipSep(List<string> tokens, ref int pos)
    {
        while (pos < tokens.Count && tokens[pos] == "-") pos++;
    }

    private static int ParseMonth(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
            throw new FormatException("Expected month name");
        var tok = tokens[pos];
        // Month abbreviations must be exactly 3 characters per IETF spec
        if (tok.Length != 3 || !MonthMap.TryGetValue(tok, out var month))
            throw new FormatException($"Invalid month: '{tok}'");
        pos++;
        return month;
    }

    private static int ParseYear(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count || !int.TryParse(tokens[pos], out var year))
            throw new FormatException("Expected year");
        var yearStr = tokens[pos];
        // Year must be exactly 2 or 4+ digits per IETF spec
        if (yearStr.Length == 1 || yearStr.Length == 3)
            throw new FormatException($"Year must be 2 or 4+ digits: '{yearStr}'");
        pos++;
        return year;
    }

    private static void ParseTime(List<string> tokens, ref int pos,
        out int hour, out int minute, out double second)
    {
        second = 0;
        if (pos >= tokens.Count)
            throw new FormatException("Expected time");
        var timeTok = tokens[pos];
        // Time format: H:MM or HH:MM or H:MM:SS or HH:MM:SS[.frac]
        var timeParts = timeTok.Split(':');
        if (timeParts.Length < 2)
            throw new FormatException($"Invalid time: '{timeTok}'");
        // Validate digit counts: hour 1-2, minute exactly 2, second exactly 2 (before decimal)
        if (timeParts[0].Length < 1 || timeParts[0].Length > 2)
            throw new FormatException($"Hour must be 1-2 digits: '{timeParts[0]}'");
        if (timeParts[1].Length != 2)
            throw new FormatException($"Minutes must be exactly 2 digits: '{timeParts[1]}'");
        hour = int.Parse(timeParts[0], System.Globalization.CultureInfo.InvariantCulture);
        minute = int.Parse(timeParts[1], System.Globalization.CultureInfo.InvariantCulture);
        if (timeParts.Length > 2)
        {
            var secStr = timeParts[2];
            // Seconds: 2 digits before optional decimal point
            var dotIdx = secStr.IndexOf('.');
            var wholeSecStr = dotIdx >= 0 ? secStr[..dotIdx] : secStr;
            if (wholeSecStr.Length != 2)
                throw new FormatException($"Seconds must be exactly 2 digits: '{wholeSecStr}'");
            if (dotIdx >= 0 && dotIdx + 1 >= secStr.Length)
                throw new FormatException($"Decimal point must be followed by digits");
            second = double.Parse(secStr, System.Globalization.CultureInfo.InvariantCulture);
        }
        pos++;
    }

    private static TimeSpan? ParseTimezone(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count) return null;
        var tok = tokens[pos];

        // Named timezone (GMT, UTC, EST, etc.)
        if (TzNameMap.TryGetValue(tok, out var tzHours))
        {
            pos++;
            SkipComment(tokens, ref pos);
            return TimeSpan.FromHours(tzHours);
        }

        // Positive offset: +HHMM, +HH:MM, +HH, +H
        if (tok.Length >= 2 && tok[0] == '+')
        {
            pos++;
            var result = ParseOffsetDigits(tok[1..], 1);
            SkipComment(tokens, ref pos);
            return result;
        }

        // Negative offset: "-" token followed by digits (e.g., "-" "05" ":" "00" or "-" "0500" or "-" "5")
        if (tok == "-" && pos + 1 < tokens.Count)
        {
            var nextTok = tokens[pos + 1];
            // Must be all digits (possibly with embedded colon from tokenizer)
            if (nextTok.Length > 0 && (char.IsDigit(nextTok[0]) || nextTok.Contains(':', StringComparison.Ordinal)))
            {
                // Check if it looks like an offset (not a year)
                // If it contains ':' it's definitely an offset
                // If it's 1-2 digits, it's an offset hour
                // If it's 3-4 digits, it's HHMM or HMM
                if (nextTok.Contains(':', StringComparison.Ordinal) || nextTok.Length <= 4)
                {
                    pos += 2;
                    // Check for ":" "MM" pattern (when the offset was tokenized as separate pieces)
                    string offsetStr = nextTok;
                    if (!offsetStr.Contains(':', StringComparison.Ordinal) && offsetStr.Length <= 2 &&
                        pos + 1 < tokens.Count && tokens[pos] == ":" &&
                        pos + 1 < tokens.Count && tokens[pos + 1].Length == 2 &&
                        int.TryParse(tokens[pos + 1], out _))
                    {
                        offsetStr = offsetStr + ":" + tokens[pos + 1];
                        pos += 2;
                    }
                    var result = ParseOffsetDigits(offsetStr, -1);
                    SkipComment(tokens, ref pos);
                    return result;
                }
            }
        }

        return null;
    }

    private static TimeSpan ParseOffsetDigits(string digits, int sign)
    {
        int h, m = 0;
        if (digits.Contains(':', StringComparison.Ordinal))
        {
            var parts = digits.Split(':');
            h = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            if (parts.Length > 1 && parts[1].Length > 0)
            {
                if (parts[1].Length != 2)
                    throw new FormatException($"Timezone offset minutes must be exactly 2 digits: '{parts[1]}'");
                m = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        else if (digits.Length == 4)
        {
            h = int.Parse(digits[..2], System.Globalization.CultureInfo.InvariantCulture);
            m = int.Parse(digits[2..], System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (digits.Length == 3)
        {
            h = int.Parse(digits[..1], System.Globalization.CultureInfo.InvariantCulture);
            m = int.Parse(digits[1..], System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (digits.Length >= 1 && digits.Length <= 2)
        {
            h = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        }
        else
            throw new FormatException($"Invalid timezone offset digits: '{digits}'");
        if (m >= 60)
            throw new FormatException($"Timezone offset minutes must be < 60: '{digits}'");
        return new TimeSpan(sign * h, sign * m, 0);
    }

    private static void SkipComment(List<string> tokens, ref int pos)
    {
        if (pos < tokens.Count && tokens[pos] == "(")
        {
            pos++;
            // Collect comment content — must be a known timezone name
            bool hasContent = false;
            while (pos < tokens.Count && tokens[pos] != ")")
            {
                var commentTok = tokens[pos];
                if (commentTok.Length > 0 && char.IsLetter(commentTok[0]))
                {
                    if (!TzNameMap.ContainsKey(commentTok))
                        throw new FormatException($"Unknown timezone name in comment: '{commentTok}'");
                    hasContent = true;
                }
                pos++;
            }
            if (!hasContent)
                throw new FormatException("Empty timezone comment");
            if (pos < tokens.Count) pos++;
        }
    }
}
