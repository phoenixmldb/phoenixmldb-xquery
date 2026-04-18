using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:doc($uri) as document-node()?
/// </summary>
public sealed class DocFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "doc");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Document, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "uri"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();
        if (uri == null)
            return ValueTask.FromResult<object?>(null);

        // Validate URI syntax — FODC0005 for invalid URIs
        if (uri.Length > 0 && !Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out _))
            throw new XQueryRuntimeException("FODC0005",
                $"The URI '{uri}' passed to fn:doc() is not a valid URI");

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            // Resolve against the static base URI of the calling module (XSLT 3.0 §13.2).
            // doc('') returns the module itself; relative URIs resolve against its location.
            if (queryContext.StaticBaseUri != null)
            {
                if (uri.Length == 0)
                    uri = queryContext.StaticBaseUri;
                else if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                {
                    if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                        uri = new Uri(baseUri, uri).AbsoluteUri;
                }
            }

            object? doc;
            try
            {
                doc = queryContext.DocumentResolver.ResolveDocument(uri);
            }
            catch (Exception ex)
            {
                throw new XQueryRuntimeException("FODC0002",
                    $"Error retrieving resource identified by URI '{uri}': {ex.Message}");
            }

            if (doc == null)
                throw new XQueryRuntimeException("FODC0002",
                    $"No document could be retrieved for URI '{uri}'");

            return ValueTask.FromResult<object?>(doc);
        }

        // No document resolver available — cannot retrieve any document
        throw new XQueryRuntimeException("FODC0002",
            $"No document resolver is available to retrieve URI '{uri}'");
    }
}

/// <summary>
/// fn:doc-available($uri) as xs:boolean
/// </summary>
public sealed class DocAvailableFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "doc-available");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "uri"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (arg == null)
            return ValueTask.FromResult<object?>(false);
        // XPTY0004: argument must be xs:string or xs:anyURI
        var uri = arg switch
        {
            string s => s,
            Xdm.XsAnyUri u => u.Value,
            _ => throw new XQueryException("XPTY0004", $"Expected xs:string for fn:doc-available, got {arg.GetType().Name}")
        };

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            if (queryContext.StaticBaseUri != null)
            {
                if (uri.Length == 0)
                    uri = queryContext.StaticBaseUri;
                else if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
                {
                    if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                        uri = new Uri(baseUri, uri).AbsoluteUri;
                }
            }
            var available = queryContext.DocumentResolver.IsDocumentAvailable(uri);
            return ValueTask.FromResult<object?>(available);
        }

        return ValueTask.FromResult<object?>(false);
    }
}

/// <summary>
/// fn:collection($arg) as item()*
/// </summary>
public sealed class CollectionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();

        // FODC0004: invalid URI (e.g., malformed percent-escape)
        if (uri != null)
            ValidateCollectionUri(uri);

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            // Check for registered non-node collections (XQuery 3.1: fn:collection returns item()*)
            if (queryContext.DocumentResolver is XdmDocumentStore store &&
                store.TryResolveCollectionItems(uri, out var items))
            {
                return ValueTask.FromResult<object?>(items!.ToArray());
            }

            var docs = queryContext.DocumentResolver.ResolveCollection(uri).ToArray();
            if (docs.Length > 0)
                return ValueTask.FromResult<object?>(docs);
        }

        // FODC0002: collection not found
        throw new XQueryRuntimeException("FODC0002", $"Collection '{uri}' not found");
    }

    internal static void ValidateCollectionUri(string uri)
    {
        for (int i = 0; i < uri.Length; i++)
        {
            if (uri[i] == '%')
            {
                if (i + 2 >= uri.Length || !Uri.IsHexDigit(uri[i + 1]) || !Uri.IsHexDigit(uri[i + 2]))
                    throw new XQueryRuntimeException("FODC0004", $"Invalid collection URI: '{uri}'");
                i += 2;
            }
        }
    }
}

/// <summary>
/// fn:collection() as item()*  (default collection)
/// </summary>
public sealed class Collection0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            // Check for registered non-node collections (XQuery 3.1: fn:collection returns item()*)
            if (queryContext.DocumentResolver is XdmDocumentStore store &&
                store.TryResolveCollectionItems(null, out var items))
            {
                return ValueTask.FromResult<object?>(items!.ToArray());
            }

            var docs = queryContext.DocumentResolver.ResolveCollection(null).ToArray();
            if (docs.Length > 0)
                return ValueTask.FromResult<object?>(docs);
        }

        // FODC0002: no default collection is available
        throw new XQueryRuntimeException("FODC0002", "No default collection is available");
    }
}

/// <summary>
/// fn:uri-collection($arg as xs:string?) as xs:anyURI*
/// Returns the URIs of documents in a collection.
/// </summary>
public sealed class UriCollectionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "uri-collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();

        // FODC0004: invalid URI
        if (uri != null)
            CollectionFunction.ValidateCollectionUri(uri);

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            var docs = queryContext.DocumentResolver.ResolveCollection(uri);
            var docsList = docs.ToList();
            if (docsList.Count > 0)
            {
                var uris = new List<object?>();
                foreach (var doc in docsList)
                {
                    string? docUri = null;
                    if (doc is Xdm.Nodes.XdmDocument xdmDoc)
                        docUri = xdmDoc.DocumentUri;
                    else if (doc is Xdm.Nodes.XdmNode node)
                        docUri = node.BaseUri;

                    if (docUri != null)
                        uris.Add(new Xdm.XsAnyUri(docUri));
                }
                return ValueTask.FromResult<object?>(uris.ToArray());
            }
        }

        // FODC0002: collection not found
        throw new XQueryRuntimeException("FODC0002", $"URI collection '{uri}' not found");
    }
}

/// <summary>
/// fn:uri-collection() as xs:anyURI*  (default collection)
/// </summary>
public sealed class UriCollection0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "uri-collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            var docs = queryContext.DocumentResolver.ResolveCollection(null);
            var docsList = docs.ToList();
            if (docsList.Count > 0)
            {
                var uris = new List<object?>();
                foreach (var doc in docsList)
                {
                    string? docUri = null;
                    if (doc is Xdm.Nodes.XdmDocument xdmDoc)
                        docUri = xdmDoc.DocumentUri;
                    else if (doc is Xdm.Nodes.XdmNode node)
                        docUri = node.BaseUri;

                    if (docUri != null)
                        uris.Add(new Xdm.XsAnyUri(docUri));
                }
                return ValueTask.FromResult<object?>(uris.ToArray());
            }
        }

        // FODC0002: no default URI collection is available
        throw new XQueryRuntimeException("FODC0002", "No default URI collection is available");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// JSON document functions (XPath 3.1 §14.8)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Helper to convert System.Text.Json elements to XDM maps, arrays, and atomic values.
/// </summary>
/// <summary>
/// Options for fn:parse-json.
/// </summary>
internal sealed class ParseJsonOptions
{
    public string Duplicates { get; set; } = "use-first";
    public bool Escape { get; set; }
    public XQueryFunction? Fallback { get; set; }

    /// <summary>
    /// Extracts parse-json options from the options map argument.
    /// </summary>
    internal static ParseJsonOptions FromMap(object? optionsArg)
    {
        var result = new ParseJsonOptions();
        if (optionsArg is not IDictionary<object, object?> map)
            return result;

        foreach (var kv in map)
        {
            var key = QueryExecutionContext.Atomize(kv.Key)?.ToString();
            var rawVal = kv.Value;
            // Don't atomize function items (they throw FOTY0013) — handled separately by "fallback"
            object? val = rawVal is XQueryFunction ? rawVal : QueryExecutionContext.Atomize(rawVal);
            switch (key)
            {
                case "duplicates":
                {
                    var s = val?.ToString();
                    if (s is not ("use-first" or "use-last" or "reject"))
                        throw new XQueryRuntimeException("FOJS0005",
                            $"Invalid value '{s}' for option 'duplicates' — " +
                            "must be 'use-first', 'use-last', or 'reject'");
                    result.Duplicates = s;
                    break;
                }
                case "escape":
                {
                    if (val is bool b)
                        result.Escape = b;
                    else
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Option 'escape' requires xs:boolean, got {val?.GetType().Name ?? "null"}");
                    break;
                }
                case "fallback":
                {
                    // The value must be a function item (arity 1).
                    if (rawVal is XQueryFunction fn)
                    {
                        if (fn.Arity != 1)
                            throw new XQueryRuntimeException("XPTY0004",
                                $"Option 'fallback' requires a function with arity 1, got arity {fn.Arity}");
                        result.Fallback = fn;
                    }
                    else
                    {
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Option 'fallback' requires a function item, got {rawVal?.GetType().Name ?? "null"}");
                    }
                    break;
                }
                case "liberal":
                {
                    if (val is not bool)
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Option 'liberal' requires xs:boolean, got {val?.GetType().Name ?? "null"}");
                    break;
                }
                case "spec":
                    // Ignored per spec — retained for backwards compatibility
                    break;
                default:
                    // Unknown options are silently ignored per spec
                    break;
            }
        }

        if (result.Escape && result.Fallback != null)
            throw new XQueryRuntimeException("FOJS0005",
                "Options 'escape' and 'fallback' cannot both be specified");

        return result;
    }
}

internal static class JsonToXdmConverter
{
    /// <summary>
    /// Converts a <see cref="JsonElement"/> to its XDM representation:
    /// objects → Dictionary&lt;object, object?&gt; (XDM map),
    /// arrays → List&lt;object?&gt; (XDM array),
    /// strings → string, numbers → decimal or double, booleans → bool, null → null (empty sequence).
    /// </summary>
    internal static object? Convert(JsonElement element, ParseJsonOptions? options = null,
        Ast.ExecutionContext? context = null)
    {
        var opts = options ?? new ParseJsonOptions();
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var map = new Dictionary<object, object?>(XdmMapKeyComparer.Instance);
                foreach (var prop in element.EnumerateObject())
                {
                    var key = opts.Escape ? EscapeString(prop.Name) : ReplaceInvalidXmlChars(prop.Name, opts.Fallback, context);
                    var value = Convert(prop.Value, opts, context);
                    if (map.ContainsKey(key))
                    {
                        switch (opts.Duplicates)
                        {
                            case "reject":
                                throw new XQueryRuntimeException("FOJS0003",
                                    $"Duplicate key '{key}' in JSON object");
                            case "use-first":
                                // Keep the existing value
                                continue;
                            case "use-last":
                                map[key] = value;
                                break;
                        }
                    }
                    else
                    {
                        map[key] = value;
                    }
                }
                return map;
            }

            case JsonValueKind.Array:
            {
                var array = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(Convert(item, opts, context));
                }
                return array;
            }

            case JsonValueKind.String:
            {
                var s = element.GetString() ?? "";
                if (opts.Escape)
                    return EscapeString(s);
                return ReplaceInvalidXmlChars(s, opts.Fallback, context);
            }

            case JsonValueKind.Number:
                // XPath 3.1 spec: JSON numbers are always represented as xs:double
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    // PUA range used to preserve lone surrogate codepoints through System.Text.Json parsing.
    // Surrogates D800-DFFF (2048 values) are mapped to PUA F000-F7FF.
    private const int SurrogatePuaBase = 0xF000;
    private const int SurrogateRangeStart = 0xD800;
    private const int SurrogateRangeEnd = 0xDFFF;

    private static bool IsSurrogatePuaMarker(char ch) =>
        ch >= SurrogatePuaBase && ch < SurrogatePuaBase + (SurrogateRangeEnd - SurrogateRangeStart + 1);

    private static int GetOriginalSurrogate(char ch) =>
        SurrogateRangeStart + (ch - SurrogatePuaBase);

    /// <summary>
    /// When escape=true, re-encode characters that have JSON escape forms using backslash notation.
    /// Per spec: backslash itself is \\ and characters with standard JSON escapes are escaped.
    /// Characters that are valid in XML are left as-is. Only the special JSON escapes are retained.
    /// Lone surrogates (encoded as PUA markers) are output as \uXXXX escape sequences.
    /// </summary>
    private static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (IsSurrogatePuaMarker(ch))
            {
                sb.Append($"\\u{GetOriginalSurrogate(ch):X4}");
                continue;
            }
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20 || (ch >= 0x7F && ch <= 0x9F))
                    {
                        // Control characters: use \uXXXX
                        sb.Append($"\\u{(int)ch:X4}");
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Replaces characters that are not valid in XML 1.0 with U+FFFD (or calls the fallback function).
    /// Invalid XML 1.0 chars: U+0000-U+0008, U+000B-U+000C, U+000E-U+001F, U+FFFE, U+FFFF,
    /// and lone surrogates U+D800-U+DFFF.
    /// </summary>
    internal static string ReplaceInvalidXmlChars(string s, XQueryFunction? fallback = null,
        Ast.ExecutionContext? context = null)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];

            // Handle surrogate pairs: supplementary characters U+10000-U+10FFFF are valid in XML 1.0
            if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                sb.Append(ch);
                sb.Append(s[i + 1]);
                i++; // skip the low surrogate
                continue;
            }

            // Detect PUA markers for lone surrogates (set by PreProcessSurrogates)
            if (IsSurrogatePuaMarker(ch))
            {
                var originalCodepoint = GetOriginalSurrogate(ch);
                if (fallback != null && context != null)
                {
                    var hex = $"\\u{originalCodepoint:X4}";
                    try
                    {
                        var result = fallback.InvokeAsync([hex], context).AsTask().GetAwaiter().GetResult();
                        sb.Append(result?.ToString() ?? "");
                    }
                    catch (XQueryRuntimeException) { throw; }
                    catch (Exception ex)
                    {
                        throw new XQueryRuntimeException("FOJS0001",
                            $"Fallback function raised an error: {ex.Message}");
                    }
                }
                else
                {
                    sb.Append('\uFFFD');
                }
            }
            else if (IsInvalidXmlChar(ch))
            {
                if (fallback != null && context != null)
                {
                    // Call the fallback function with the hex representation of the codepoint
                    var hex = $"\\u{(int)ch:X4}";
                    try
                    {
                        var result = fallback.InvokeAsync([hex], context).AsTask().GetAwaiter().GetResult();
                        sb.Append(result?.ToString() ?? "");
                    }
                    catch (XQueryRuntimeException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new XQueryRuntimeException("FOJS0001",
                            $"Fallback function raised an error: {ex.Message}");
                    }
                }
                else
                {
                    // Default: replace with U+FFFD (replacement character)
                    sb.Append('\uFFFD');
                }
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns true if the character is not valid in XML 1.0.
    /// </summary>
    private static bool IsInvalidXmlChar(char ch)
    {
        // XML 1.0 valid chars: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
        // Note: supplementary chars (U+10000+) are handled as surrogate pairs before this is called
        if (ch == 0x9 || ch == 0xA || ch == 0xD) return false;
        if (ch >= 0x20 && ch <= 0xD7FF) return false;
        if (ch >= 0xE000 && ch <= 0xFFFD) return false;
        return true;
    }

    /// <summary>
    /// Pre-processes JSON text to replace lone surrogates (which System.Text.Json rejects)
    /// with PUA marker characters that preserve the original codepoint information.
    /// Surrogates D800-DFFF are mapped to PUA range F000-F7FF.
    /// </summary>
    internal static string PreProcessSurrogates(string jsonText)
    {
        // Match \uXXXX patterns in JSON strings and fix lone surrogates
        return Regex.Replace(jsonText, @"\\u([0-9A-Fa-f]{4})", match =>
        {
            var hex = match.Groups[1].Value;
            var codePoint = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);

            if (codePoint >= 0xD800 && codePoint <= 0xDBFF)
            {
                // High surrogate — check if followed by a valid low surrogate
                var afterMatch = jsonText.AsSpan(match.Index + match.Length);
                if (afterMatch.Length >= 6 && afterMatch[0] == '\\' && afterMatch[1] == 'u')
                {
                    var lowHex = afterMatch.Slice(2, 4);
                    if (int.TryParse(lowHex, System.Globalization.NumberStyles.HexNumber, null, out var lowCode)
                        && lowCode >= 0xDC00 && lowCode <= 0xDFFF)
                    {
                        // Valid surrogate pair — leave as-is, System.Text.Json handles it
                        return match.Value;
                    }
                }
                // Lone high surrogate — map to PUA marker
                var puaChar = (char)(SurrogatePuaBase + (codePoint - SurrogateRangeStart));
                return $"\\u{(int)puaChar:X4}";
            }
            if (codePoint >= 0xDC00 && codePoint <= 0xDFFF)
            {
                // Lone low surrogate — check if preceded by a high surrogate
                if (match.Index >= 6)
                {
                    var before = jsonText.AsSpan(match.Index - 6, 6);
                    if (before[0] == '\\' && before[1] == 'u')
                    {
                        var highHex = before.Slice(2, 4);
                        if (int.TryParse(highHex, System.Globalization.NumberStyles.HexNumber, null, out var highCode)
                            && highCode >= 0xD800 && highCode <= 0xDBFF)
                        {
                            // Part of a valid pair — leave as-is
                            return match.Value;
                        }
                    }
                }
                // Lone low surrogate — map to PUA marker
                var puaChar = (char)(SurrogatePuaBase + (codePoint - SurrogateRangeStart));
                return $"\\u{(int)puaChar:X4}";
            }
            return match.Value;
        });
    }
}

/// <summary>
/// fn:parse-json($json-text as xs:string) as item()?
/// Parses a JSON string and returns the corresponding XDM value
/// (map for objects, array for arrays, atomic for primitives).
/// </summary>
public sealed class ParseJsonFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-json");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ParseJsonCore(arguments[0]?.ToString(), null, context);
    }

    internal static ValueTask<object?> ParseJsonCore(string? jsonText, ParseJsonOptions? options,
        Ast.ExecutionContext? context)
    {
        if (jsonText == null)
            return ValueTask.FromResult<object?>(null);

        var opts = options ?? new ParseJsonOptions();

        try
        {
            // Pre-process to handle lone surrogates that System.Text.Json rejects
            var processed = JsonToXdmConverter.PreProcessSurrogates(jsonText);
            using var doc = JsonDocument.Parse(processed);
            // JsonDocument is disposable; we must materialize the result before disposing.
            var result = JsonToXdmConverter.Convert(doc.RootElement, opts, context);
            return ValueTask.FromResult(result);
        }
        catch (XQueryRuntimeException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"The string supplied to fn:parse-json() is not valid JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// fn:parse-json($json-text as xs:string, $options as map(*)) as item()?
/// </summary>
public sealed class ParseJson2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-json");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "options"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var opts = ParseJsonOptions.FromMap(arguments[1]);
        return ParseJsonFunction.ParseJsonCore(arguments[0]?.ToString(), opts, context);
    }
}

/// <summary>
/// fn:json-doc($href as xs:string) as item()?
/// Reads a JSON file from the given URI and returns the corresponding XDM value.
/// </summary>
public sealed class JsonDocFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "json-doc");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString }];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var href = arguments[0]?.ToString();
        if (href == null)
            return null;

        // Resolve relative URI against static base URI
        if (context is QueryExecutionContext queryContext && queryContext.StaticBaseUri != null)
        {
            if (!Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                    href = new Uri(baseUri, href).AbsoluteUri;
            }
        }

        string jsonText;
        try
        {
            // Support file:// URIs and plain file paths
            var filePath = href;
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile)
                filePath = uri.LocalPath;

            jsonText = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"Error reading JSON resource '{href}': {ex.Message}");
        }

        try
        {
            var processed = JsonToXdmConverter.PreProcessSurrogates(jsonText);
            using var doc = JsonDocument.Parse(processed);
            var result = JsonToXdmConverter.Convert(doc.RootElement);
            return result;
        }
        catch (JsonException ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"The resource '{href}' does not contain valid JSON: {ex.Message}");
        }
    }
}

/// <summary>fn:json-doc($href, $options) as item()?</summary>
public sealed class JsonDoc2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "json-doc");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "options"), Type = XdmSequenceType.Item }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Parse and validate options
        ParseJsonOptions? opts = null;
        if (arguments[1] is Dictionary<object, object?> map)
            opts = ParseJsonOptions.FromMap(map);

        var href = arguments[0]?.ToString();
        if (href == null)
            return null;

        // Resolve relative URI against static base URI
        if (context is QueryExecutionContext queryContext && queryContext.StaticBaseUri != null)
        {
            if (!Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                if (Uri.TryCreate(queryContext.StaticBaseUri, UriKind.Absolute, out var baseUri))
                    href = new Uri(baseUri, href).AbsoluteUri;
            }
        }

        string jsonText;
        try
        {
            var filePath = href;
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile)
                filePath = uri.LocalPath;

            jsonText = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"Error reading JSON resource '{href}': {ex.Message}");
        }

        try
        {
            var processed = JsonToXdmConverter.PreProcessSurrogates(jsonText);
            using var doc = JsonDocument.Parse(processed);
            var result = JsonToXdmConverter.Convert(doc.RootElement, opts, context);
            return result;
        }
        catch (JsonException ex)
        {
            throw new XQueryRuntimeException("FOJS0001",
                $"The resource '{href}' does not contain valid JSON: {ex.Message}");
        }
    }
}

/// <summary>fn:load-xquery-module($module-uri as xs:string) as map(*)</summary>
public sealed class LoadXQueryModuleFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "load-xquery-module");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "module-uri"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Stub: return empty map for now
        throw new XQueryRuntimeException("FOQM0006",
            $"Module '{arguments[0]}' cannot be loaded");
    }
}

/// <summary>fn:load-xquery-module($module-uri, $options) as map(*)</summary>
public sealed class LoadXQueryModule2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "load-xquery-module");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "module-uri"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "options"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return new LoadXQueryModuleFunction().InvokeAsync([arguments[0]], context);
    }
}
