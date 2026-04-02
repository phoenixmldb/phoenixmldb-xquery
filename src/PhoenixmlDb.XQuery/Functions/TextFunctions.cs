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
        if (string.IsNullOrEmpty(href)) return null;
        try
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile)
                return await File.ReadAllTextAsync(uri.LocalPath).ConfigureAwait(false);
            if (File.Exists(href))
                return await File.ReadAllTextAsync(href).ConfigureAwait(false);
        }
        catch (IOException) { }
        return null;
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
        if (string.IsNullOrEmpty(href)) return null;
        var encoding = System.Text.Encoding.GetEncoding(arguments[1]?.ToString() ?? "utf-8");
        try
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile)
                return await File.ReadAllTextAsync(uri.LocalPath, encoding).ConfigureAwait(false);
            if (File.Exists(href))
                return await File.ReadAllTextAsync(href, encoding).ConfigureAwait(false);
        }
        catch (IOException) { }
        return null;
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
        if (string.IsNullOrEmpty(href)) return ValueTask.FromResult<object?>(false);
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile)
            return ValueTask.FromResult<object?>(File.Exists(uri.LocalPath));
        return ValueTask.FromResult<object?>(File.Exists(href));
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
                    if (group.Index - match.Index > mPos)
                        sb.Append(System.Security.SecurityElement.Escape(match.Value[(mPos)..(group.Index - match.Index)]));
                    sb.Append($"<fn:group nr=\"{g}\">").Append(System.Security.SecurityElement.Escape(group.Value)).Append("</fn:group>");
                    mPos = group.Index - match.Index + group.Length;
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

        if (DateTimeOffset.TryParseExact(value, formats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var dto))
        {
            return ValueTask.FromResult<object?>(new Xdm.XsDateTime(dto, true));
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
