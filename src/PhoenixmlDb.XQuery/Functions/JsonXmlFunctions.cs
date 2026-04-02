using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

// ─── fn:xml-to-json (1-arg) ────────────────────────────────────────────────

/// <summary>
/// fn:xml-to-json($input as node()?) as xs:string?
/// Converts the XML representation of JSON (using the XPath functions namespace)
/// back to a JSON string.
/// </summary>
public sealed class XmlToJsonFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "xml-to-json");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalNode }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0];
        if (input is null)
            return ValueTask.FromResult<object?>(null);
        // XPTY0004: xml-to-json expects a single node, not a sequence
        if (input is object?[] arr && arr.Length > 1)
            throw new XQueryException("XPTY0004", "A sequence of " + arr.Length + " items is not allowed as the first argument of xml-to-json()");

        var store = context.NodeStore;
        if (store is null)
            throw new XQueryException("FOJS0006", "xml-to-json requires a node store");

        var elem = ResolveToElement(input, store);
        if (elem is null)
            return ValueTask.FromResult<object?>(null);

        var sb = new StringBuilder();
        SerializeJsonElement(elem, store, sb);
        return ValueTask.FromResult<object?>(sb.ToString());
    }

    internal static XdmElement? ResolveToElement(object? input, INodeStore store)
    {
        if (input is XdmElement el)
            return el;
        if (input is XdmDocument doc)
        {
            // Find first element child, but check for multiple elements (FOJS0006)
            XdmElement? first = null;
            foreach (var childId in doc.Children)
            {
                if (store.GetNode(childId) is XdmElement child)
                {
                    if (first != null)
                        throw new XQueryException("FOJS0006", "xml-to-json input contains multiple element children");
                    first = child;
                }
            }
            return first;
        }
        return null;
    }

    internal static void SerializeJsonElement(XdmElement elem, INodeStore store, StringBuilder sb)
    {
        // Elements must be in the http://www.w3.org/2005/xpath-functions namespace
        var localName = elem.LocalName;

        switch (localName)
        {
            case "null":
            {
                // Null must have no non-whitespace text content and no element children
                ValidateNoElementChildren(elem, store, "null");
                var nullText = GetTextContent(elem, store).Trim();
                if (nullText.Length > 0)
                    throw new XQueryException("FOJS0006", "null element must have no content");
                ValidateAttributes(elem, store, "null", ["key", "escaped-key"]);
                sb.Append("null");
                break;
            }

            case "boolean":
            {
                ValidateNoElementChildren(elem, store, "boolean");
                ValidateAttributes(elem, store, "boolean", ["key", "escaped-key"]);
                var text = GetTextContent(elem, store).Trim();
                // Accepts "true", "false", "1", "0"
                sb.Append(text switch
                {
                    "true" or "1" => "true",
                    "false" or "0" => "false",
                    _ => throw new XQueryException("FOJS0006", $"Invalid boolean value: '{text}'")
                });
                break;
            }

            case "number":
            {
                ValidateNoElementChildren(elem, store, "number");
                ValidateAttributes(elem, store, "number", ["key", "escaped-key"]);
                var text = GetTextContent(elem, store).Trim();
                // Validate it's a valid JSON number
                if (!IsValidJsonNumber(text))
                    throw new XQueryException("FOJS0006", $"Invalid number value: '{text}'");
                sb.Append(text);
                break;
            }

            case "string":
            {
                // String elements must contain only text nodes (no element children)
                ValidateNoElementChildren(elem, store, "string");
                var escaped = GetAttributeValue(elem, "escaped", store);
                ValidateBooleanAttribute(escaped, "escaped");
                ValidateAttributes(elem, store, "string", ["key", "escaped-key", "escaped"]);
                var isEscaped = IsTruthy(escaped);
                var text = GetTextContent(elem, store);

                sb.Append('"');
                if (isEscaped)
                    AppendValidatedEscapedJsonString(text, sb);
                else
                    AppendJsonString(text, sb);
                sb.Append('"');
                break;
            }

            case "array":
            {
                ValidateAttributes(elem, store, "array", ["key", "escaped-key"]);
                // Array must not contain non-whitespace text
                ValidateNoSignificantText(elem, store, "array");
                sb.Append('[');
                var first = true;
                foreach (var childId in elem.Children)
                {
                    var child = store.GetNode(childId);
                    if (child is XdmElement childElem && childElem.Namespace == NamespaceId.Fn)
                    {
                        if (!first)
                            sb.Append(',');
                        first = false;
                        SerializeJsonElement(childElem, store, sb);
                    }
                    else if (child is XdmElement childElem2)
                    {
                        // Element not in fn namespace — try processing anyway
                        // (namespace IDs may differ in RTF node stores)
                        if (!first)
                            sb.Append(',');
                        first = false;
                        SerializeJsonElement(childElem2, store, sb);
                    }
                }
                sb.Append(']');
                break;
            }

            case "map":
            {
                ValidateAttributes(elem, store, "map", ["key", "escaped-key"]);
                // Map must not contain non-whitespace text
                ValidateNoSignificantText(elem, store, "map");
                sb.Append('{');
                var first = true;
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var childId in elem.Children)
                {
                    var child = store.GetNode(childId);
                    if (child is XdmElement childElem && childElem.Namespace == NamespaceId.Fn)
                    {
                        if (!first)
                            sb.Append(',');
                        first = false;

                        // Get the key
                        var key = GetAttributeValue(childElem, "key", store);
                        if (key is null)
                            throw new XQueryException("FOJS0006", "Map entry missing 'key' attribute");

                        var escapedKey = GetAttributeValue(childElem, "escaped-key", store);
                        ValidateBooleanAttribute(escapedKey, "escaped-key");
                        var isEscapedKey = IsTruthy(escapedKey);

                        // Duplicate detection uses decoded key values
                        var decodedKey = DecodeJsonKey(key, isEscapedKey);
                        if (!seenKeys.Add(decodedKey))
                            throw new XQueryException("FOJS0006", $"Duplicate key in map: '{key}'");

                        sb.Append('"');
                        if (isEscapedKey)
                            AppendValidatedEscapedJsonString(key, sb);
                        else
                            AppendJsonString(key, sb);
                        sb.Append('"');
                        sb.Append(':');
                        SerializeJsonElement(childElem, store, sb);
                    }
                    else if (child is XdmElement childElem2)
                    {
                        // Element not in fn namespace — try processing anyway
                        // (namespace IDs may differ in RTF node stores)
                        if (!first)
                            sb.Append(',');
                        first = false;

                        var key2 = GetAttributeValue(childElem2, "key", store);
                        if (key2 is null)
                            throw new XQueryException("FOJS0006", "Map entry missing 'key' attribute");

                        var escapedKey2 = GetAttributeValue(childElem2, "escaped-key", store);
                        ValidateBooleanAttribute(escapedKey2, "escaped-key");
                        var isEscapedKey2 = IsTruthy(escapedKey2);

                        var decodedKey2 = DecodeJsonKey(key2, isEscapedKey2);
                        if (!seenKeys.Add(decodedKey2))
                            throw new XQueryException("FOJS0006", $"Duplicate key in map: '{key2}'");

                        sb.Append('"');
                        if (isEscapedKey2)
                            AppendValidatedEscapedJsonString(key2, sb);
                        else
                            AppendJsonString(key2, sb);
                        sb.Append('"');
                        sb.Append(':');
                        SerializeJsonElement(childElem2, store, sb);
                    }
                }
                sb.Append('}');
                break;
            }

            default:
                throw new XQueryException("FOJS0006", $"Unknown JSON element type: '{localName}'");
        }
    }

    /// <summary>
    /// Gets the concatenated text content of an element (ignoring comments and PIs).
    /// </summary>
    internal static string GetTextContent(XdmElement elem, INodeStore store)
    {
        var sb = new StringBuilder();
        foreach (var childId in elem.Children)
        {
            var child = store.GetNode(childId);
            if (child is XdmText text)
                sb.Append(text.Value);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Gets an attribute value by local name (no namespace).
    /// </summary>
    internal static string? GetAttributeValue(XdmElement elem, string localName, INodeStore store)
    {
        foreach (var attrId in elem.Attributes)
        {
            var attr = store.GetNode(attrId) as XdmAttribute;
            if (attr != null && attr.LocalName == localName && attr.Namespace == NamespaceId.None)
                return attr.Value;
        }
        return null;
    }

    /// <summary>
    /// Checks if a string attribute value is truthy (true/1, ignoring whitespace).
    /// </summary>
    internal static bool IsTruthy(string? value)
    {
        if (value is null)
            return false;
        var trimmed = value.Trim();
        return trimmed == "true" || trimmed == "1";
    }

    /// <summary>
    /// Appends a string to the JSON output, escaping characters that need it per JSON spec.
    /// Used when escaped="false" (or absent) — the input is plain text.
    /// </summary>
    internal static void AppendJsonString(string text, StringBuilder sb)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
    }

    /// <summary>
    /// Appends a string to the JSON output when escaped="true" — the input already contains
    /// JSON escape sequences like \n, \uXXXX etc. We pass through backslash sequences as-is
    /// but still need to escape any characters that would be invalid in a JSON string.
    /// </summary>
    internal static void AppendEscapedJsonString(string text, StringBuilder sb)
    {
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                // Pass through recognized JSON escape sequences
                var next = text[i + 1];
                switch (next)
                {
                    case '"':
                    case '\\':
                    case '/':
                    case 'b':
                    case 'f':
                    case 'n':
                    case 'r':
                    case 't':
                        sb.Append(c);
                        sb.Append(next);
                        i++;
                        continue;
                    case 'u' when i + 5 < text.Length:
                        // \uXXXX — pass through
                        sb.Append(text, i, 6);
                        i += 5;
                        continue;
                }
            }

            // Escape characters that need it
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
    }

    /// <summary>
    /// Validates that a string is a valid JSON number.
    /// </summary>
    internal static bool IsValidJsonNumber(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        // Allow a superset: optional minus, digits, optional dot+digits, optional E/e +/- digits
        int i = 0;
        if (i < text.Length && text[i] == '-')
            i++;
        if (i >= text.Length)
            return false;

        // Integer part (allow leading zeros for lenient parsing)
        bool hasDigit = false;
        while (i < text.Length && text[i] >= '0' && text[i] <= '9')
        { i++; hasDigit = true; }

        // Fractional part
        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            { i++; hasDigit = true; }
        }

        if (!hasDigit)
            return false;

        // Exponent part
        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            i++;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
                i++;
            bool hasExpDigit = false;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            { i++; hasExpDigit = true; }
            if (!hasExpDigit)
                return false;
        }

        return i == text.Length;
    }

    /// <summary>
    /// Validates that an element has no child elements (only text/comments/PIs allowed).
    /// </summary>
    internal static void ValidateNoElementChildren(XdmElement elem, INodeStore store, string type)
    {
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmElement)
                throw new XQueryException("FOJS0006", $"{type} element must not contain child elements");
        }
    }

    /// <summary>
    /// Validates that a container element (array/map) has no non-whitespace text content.
    /// </summary>
    internal static void ValidateNoSignificantText(XdmElement elem, INodeStore store, string type)
    {
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmText text && text.Value.AsSpan().Trim().Length > 0)
                throw new XQueryException("FOJS0006", $"{type} element must not contain text content");
        }
    }

    /// <summary>
    /// Validates that only allowed attributes are present on an element.
    /// </summary>
    internal static void ValidateAttributes(XdmElement elem, INodeStore store, string type, string[] allowed)
    {
        foreach (var attrId in elem.Attributes)
        {
            var attr = store.GetNode(attrId) as XdmAttribute;
            if (attr == null)
                continue;
            // Skip namespace declarations (xmlns, xmlns:prefix)
            if (attr.LocalName == "xmlns" || attr.Prefix == "xmlns")
                continue;
            if (attr.Namespace == NamespaceId.None)
            {
                bool found = false;
                foreach (var a in allowed)
                {
                    if (attr.LocalName == a)
                    { found = true; break; }
                }
                if (!found)
                    throw new XQueryException("FOJS0006", $"Invalid attribute '{attr.LocalName}' on {type} element");
            }
            else
            {
                // Check if this is an attribute in the fn namespace — reject unknown ones
                var nsUri = store.GetNamespaceUri(attr.Namespace);
                if (nsUri == "http://www.w3.org/2005/xpath-functions")
                    throw new XQueryException("FOJS0006", $"Invalid attribute in fn namespace '{attr.LocalName}' on {type} element");
            }
        }
    }

    /// <summary>
    /// Validates that a boolean-like attribute has value "true", "false", "1", or "0".
    /// </summary>
    internal static void ValidateBooleanAttribute(string? value, string attrName)
    {
        if (value is null)
            return;
        var trimmed = value.Trim();
        if (trimmed != "true" && trimmed != "false" && trimmed != "1" && trimmed != "0")
            throw new XQueryException("FOJS0006", $"Invalid value '{value}' for attribute '{attrName}'");
    }

    /// <summary>
    /// Like AppendEscapedJsonString but validates that escape sequences are valid JSON.
    /// Throws FOJS0006 for invalid escape sequences.
    /// </summary>
    internal static void AppendValidatedEscapedJsonString(string text, StringBuilder sb)
    {
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\')
            {
                if (i + 1 >= text.Length)
                    throw new XQueryException("FOJS0006", "Incomplete escape sequence at end of string");
                var next = text[i + 1];
                switch (next)
                {
                    case '"':
                    case '\\':
                    case '/':
                    case 'b':
                    case 'f':
                    case 'n':
                    case 'r':
                    case 't':
                        sb.Append(c);
                        sb.Append(next);
                        i++;
                        continue;
                    case 'u':
                        if (i + 5 >= text.Length)
                            throw new XQueryException("FOJS0006", "Incomplete \\u escape sequence");
                        // Validate hex digits
                        for (int j = i + 2; j < i + 6; j++)
                        {
                            if (!IsHexDigit(text[j]))
                                throw new XQueryException("FOJS0006", $"Invalid \\u escape sequence: '{text.Substring(i, 6)}'");
                        }
                        sb.Append(text, i, 6);
                        i += 5;
                        continue;
                    default:
                        throw new XQueryException("FOJS0006", $"Invalid escape sequence '\\{next}'");
                }
            }

            // Escape characters that need it
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    /// <summary>
    /// Decodes a JSON key to its actual string value for duplicate detection.
    /// When escaped-key is true, JSON escape sequences (\n, \uXXXX, \", \\, etc.) are decoded.
    /// When escaped-key is false, the raw key IS the decoded value.
    /// </summary>
    internal static string DecodeJsonKey(string rawKey, bool isEscaped)
    {
        if (!isEscaped)
            return rawKey;

        var sb = new StringBuilder(rawKey.Length);
        for (int i = 0; i < rawKey.Length; i++)
        {
            var c = rawKey[i];
            if (c == '\\' && i + 1 < rawKey.Length)
            {
                var next = rawKey[i + 1];
                switch (next)
                {
                    case '"':
                        sb.Append('"');
                        i++;
                        continue;
                    case '\\':
                        sb.Append('\\');
                        i++;
                        continue;
                    case '/':
                        sb.Append('/');
                        i++;
                        continue;
                    case 'b':
                        sb.Append('\b');
                        i++;
                        continue;
                    case 'f':
                        sb.Append('\f');
                        i++;
                        continue;
                    case 'n':
                        sb.Append('\n');
                        i++;
                        continue;
                    case 'r':
                        sb.Append('\r');
                        i++;
                        continue;
                    case 't':
                        sb.Append('\t');
                        i++;
                        continue;
                    case 'u' when i + 5 < rawKey.Length:
                        var hex = rawKey.Substring(i + 2, 4);
                        if (int.TryParse(hex, NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out var codePoint))
                        {
                            sb.Append((char)codePoint);
                            i += 5;
                            continue;
                        }
                        break;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}

// ─── fn:xml-to-json (2-arg) ────────────────────────────────────────────────

/// <summary>
/// fn:xml-to-json($input, $options) as xs:string? — 2-arg version
/// </summary>
public sealed class XmlToJson2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "xml-to-json");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalNode },
        new() { Name = new QName(NamespaceId.None, "options"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Validate options map
        var options = arguments[1];
        if (options is Dictionary<object, object?> map)
        {
            if (map.TryGetValue("indent", out var indentVal) && indentVal is not bool)
                throw new XQueryException("XPTY0004", "Option 'indent' must be a boolean value");
            if (map.TryGetValue("validate", out var validateVal))
            {
                if (validateVal is not bool)
                    throw new XQueryException("XPTY0004", "Option 'validate' must be a boolean value");
                if (validateVal is true)
                    throw new XQueryException("FOJS0004", "Option 'validate' is true but the processor is not schema-aware");
            }
        }

        // Delegate to 1-arg version
        return new XmlToJsonFunction().InvokeAsync(
            new object?[] { arguments[0] }, context);
    }
}

// ─── fn:json-to-xml (1-arg) ────────────────────────────────────────────────

/// <summary>
/// fn:json-to-xml($json-text as xs:string) as document-node()?
/// Converts a JSON string to the XML representation using the XPath functions namespace.
/// </summary>
public sealed class JsonToXmlFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "json-to-xml");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Node, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var jsonText = arguments[0]?.ToString();
        if (jsonText is null)
            return ValueTask.FromResult<object?>(null);

        var builder = context.NodeStore as INodeBuilder;
        if (builder is null)
            throw new XQueryException("FOJS0001", "json-to-xml requires a node builder");

        try
        {
            var doc = JsonToXmlConverter.Convert(jsonText, builder, liberal: false, duplicates: "use-first");
            return ValueTask.FromResult<object?>(doc);
        }
        catch (JsonException ex)
        {
            throw new XQueryException("FOJS0001", $"Invalid JSON: {ex.Message}");
        }
    }
}

// ─── fn:json-to-xml (2-arg) ────────────────────────────────────────────────

/// <summary>
/// fn:json-to-xml($json-text, $options) — 2-arg version
/// </summary>
public sealed class JsonToXml2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "json-to-xml");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Node, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "options"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var jsonText = arguments[0]?.ToString();
        if (jsonText is null)
            return ValueTask.FromResult<object?>(null);

        var builder = context.NodeStore as INodeBuilder;
        if (builder is null)
            throw new XQueryException("FOJS0001", "json-to-xml requires a node builder");

        // Parse options map
        var liberal = false;
        var escape = false;
        var duplicates = "use-first";
        var options = arguments[1];
        if (options is Dictionary<object, object?> map)
        {
            // liberal option: must be xs:boolean
            if (map.TryGetValue("liberal", out var liberalVal))
            {
                if (liberalVal is bool lb)
                    liberal = lb;
                else if (liberalVal is null || liberalVal is object?[] arr && arr.Length == 0)
                    throw new XQueryException("FOJS0001", "Option 'liberal' must be a boolean value, got empty sequence");
                else
                    throw new XQueryException("FOJS0001", "Option 'liberal' must be a boolean value");
            }

            // validate option: must be xs:boolean
            if (map.TryGetValue("validate", out var validateVal))
            {
                if (validateVal is true)
                    throw new XQueryException("FOJS0004", "Option 'validate' is true but the processor is not schema-aware");
                if (validateVal is bool)
                { /* false — no action needed */ }
                else if (validateVal is null || validateVal is object?[] va && va.Length == 0)
                    throw new XQueryException("XPTY0004", "Option 'validate' must be a boolean value, got empty sequence");
                else if (validateVal is string)
                    throw new XQueryException("XPTY0004", "Option 'validate' must be a boolean value");
                else if (validateVal is object?[] va2 && va2.Length > 1)
                    throw new XQueryException("XPTY0004", "Option 'validate' must be a single boolean value");
                else
                    throw new XQueryException("XPTY0004", "Option 'validate' must be a boolean value");
            }

            // escape option: must be xs:boolean
            if (map.TryGetValue("escape", out var escapeVal))
            {
                if (escapeVal is bool eb)
                    escape = eb;
                else if (escapeVal is null || escapeVal is object?[] ea && ea.Length == 0)
                    throw new XQueryException("XPTY0004", "Option 'escape' must be a boolean value, got empty sequence");
                else if (escapeVal is string)
                    throw new XQueryException("XPTY0004", "Option 'escape' must be a boolean value");
                else if (escapeVal is object?[] ea2 && ea2.Length > 1)
                    throw new XQueryException("XPTY0004", "Option 'escape' must be a single boolean value");
                else
                    throw new XQueryException("XPTY0004", "Option 'escape' must be a boolean value");
            }

            // fallback option: must be a function
            if (map.TryGetValue("fallback", out var fallbackVal))
            {
                if (fallbackVal is not Delegate && fallbackVal is not XQueryFunction)
                    throw new XQueryException("XPTY0004", "Option 'fallback' must be a function");
            }

            // duplicates option: must be xs:string with value use-first, retain, or reject
            if (map.TryGetValue("duplicates", out var dupVal))
            {
                if (dupVal is string ds)
                {
                    duplicates = ds switch
                    {
                        "use-first" or "retain" or "reject" => ds,
                        _ => throw new XQueryException("FOJS0005", $"Invalid value '{ds}' for option 'duplicates'; must be use-first, retain, or reject")
                    };
                }
                else
                    throw new XQueryException("XPTY0004", "Option 'duplicates' must be a string value");
            }
        }

        try
        {
            var doc = JsonToXmlConverter.Convert(jsonText, builder, liberal, duplicates, escape);
            return ValueTask.FromResult<object?>(doc);
        }
        catch (JsonException ex)
        {
            throw new XQueryException("FOJS0001", $"Invalid JSON: {ex.Message}");
        }
    }
}

// ─── JsonToXmlConverter ────────────────────────────────────────────────────

/// <summary>
/// Converts JSON strings to XDM document trees using the XPath functions namespace.
/// </summary>
internal static class JsonToXmlConverter
{
    private const string FnNamespaceUri = "http://www.w3.org/2005/xpath-functions";

    public static XdmDocument Convert(string json, INodeBuilder builder, bool liberal = false, string duplicates = "use-first", bool escape = false)
    {
        using var jsonDoc = JsonDocument.Parse(json,
            new JsonDocumentOptions { AllowTrailingCommas = liberal, CommentHandling = JsonCommentHandling.Skip });

        // Intern the fn namespace to get the NamespaceId for this builder
        var fnNs = builder.InternNamespace(FnNamespaceUri);

        var docId = builder.AllocateId();
        var rootElem = ConvertValue(jsonDoc.RootElement, null, builder, fnNs, duplicates, isRoot: true, escape: escape);

        var doc = new XdmDocument
        {
            Id = docId,
            Document = default,
            Children = new[] { rootElem.Id },
            DocumentElement = rootElem.Id
        };
        builder.RegisterNode(doc);
        rootElem.Parent = docId;

        return doc;
    }

    private static XdmElement ConvertValue(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, string duplicates, bool isRoot = false, bool escape = false)
    {
        return je.ValueKind switch
        {
            JsonValueKind.Object => ConvertObject(je, key, builder, fnNs, duplicates, isRoot, escape),
            JsonValueKind.Array => ConvertArray(je, key, builder, fnNs, duplicates, isRoot, escape),
            JsonValueKind.String => CreateStringElement(je, key, builder, fnNs, isRoot, escape),
            JsonValueKind.Number => CreateSimpleElement("number", je.GetRawText(), key, builder, fnNs, isRoot),
            JsonValueKind.True => CreateSimpleElement("boolean", "true", key, builder, fnNs, isRoot),
            JsonValueKind.False => CreateSimpleElement("boolean", "false", key, builder, fnNs, isRoot),
            JsonValueKind.Null => CreateNullElement(key, builder, fnNs, isRoot),
            _ => throw new XQueryException("FOJS0001", $"Unsupported JSON value kind: {je.ValueKind}")
        };
    }

    private static IReadOnlyList<NamespaceBinding> MakeFnNsDecl(NamespaceId fnNs) =>
        new[] { new NamespaceBinding("", fnNs) };

    private static XdmElement ConvertObject(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, string duplicates, bool isRoot = false, bool escape = false)
    {
        var elemId = builder.AllocateId();
        var children = new List<NodeId>();
        var attrs = new List<NodeId>();

        if (key != null)
            AddKeyAttribute(elemId, key, attrs, builder);

        var seenKeys = duplicates != "retain" ? new HashSet<string>() : null;
        foreach (var prop in je.EnumerateObject())
        {
            if (seenKeys != null && !seenKeys.Add(prop.Name))
            {
                // Duplicate key found
                if (duplicates == "reject")
                    throw new XQueryException("FOJS0003", $"Duplicate key '{prop.Name}' in JSON object");
                // use-first: skip subsequent occurrences
                continue;
            }
            var child = ConvertValue(prop.Value, prop.Name, builder, fnNs, duplicates, escape: escape);
            child.Parent = elemId;
            children.Add(child.Id);
        }

        var elem = new XdmElement
        {
            Id = elemId,
            Document = default,
            Namespace = fnNs,
            LocalName = "map",
            Prefix = null,
            Attributes = attrs,
            Children = children,
            NamespaceDeclarations = isRoot ? MakeFnNsDecl(fnNs) : ImmutableArray<NamespaceBinding>.Empty
        };
        builder.RegisterNode(elem);
        return elem;
    }

    private static XdmElement ConvertArray(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, string duplicates, bool isRoot = false, bool escape = false)
    {
        var elemId = builder.AllocateId();
        var children = new List<NodeId>();
        var attrs = new List<NodeId>();

        if (key != null)
            AddKeyAttribute(elemId, key, attrs, builder);

        foreach (var item in je.EnumerateArray())
        {
            var child = ConvertValue(item, null, builder, fnNs, duplicates, escape: escape);
            child.Parent = elemId;
            children.Add(child.Id);
        }

        var elem = new XdmElement
        {
            Id = elemId,
            Document = default,
            Namespace = fnNs,
            LocalName = "array",
            Prefix = null,
            Attributes = attrs,
            Children = children,
            NamespaceDeclarations = isRoot ? MakeFnNsDecl(fnNs) : ImmutableArray<NamespaceBinding>.Empty
        };
        builder.RegisterNode(elem);
        return elem;
    }

    /// <summary>
    /// Creates a fn:string element, adding escaped="true" attribute when the escape option is set
    /// and the JSON string contains backslash escape sequences.
    /// </summary>
    private static XdmElement CreateStringElement(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, bool isRoot, bool escape)
    {
        var interpreted = je.GetString() ?? "";
        if (!escape)
            return CreateSimpleElement("string", interpreted, key, builder, fnNs, isRoot);

        // When escape=true, check if the raw JSON string contains escape sequences
        var raw = je.GetRawText(); // includes surrounding quotes
        var hasEscapes = raw.Length > 2 && raw.AsSpan(1, raw.Length - 2).Contains('\\');
        var elem = CreateSimpleElement("string", interpreted, key, builder, fnNs, isRoot);
        if (hasEscapes)
        {
            // Add escaped="true" attribute per XSLT 3.0 §22.1.2
            AddAttribute(elem.Id, NamespaceId.None, "escaped", "true", elem.Attributes as List<NodeId> ?? new List<NodeId>(), builder);
        }
        return elem;
    }

    private static XdmElement CreateSimpleElement(string localName, string textValue, string? key, INodeBuilder builder, NamespaceId fnNs, bool isRoot = false)
    {
        var elemId = builder.AllocateId();
        var attrs = new List<NodeId>();

        if (key != null)
            AddKeyAttribute(elemId, key, attrs, builder);

        var textId = builder.AllocateId();
        var text = new XdmText
        {
            Id = textId,
            Document = default,
            Value = textValue,
            Parent = elemId
        };
        builder.RegisterNode(text);

        var elem = new XdmElement
        {
            Id = elemId,
            Document = default,
            Namespace = fnNs,
            LocalName = localName,
            Prefix = null,
            Attributes = attrs,
            Children = new[] { textId },
            NamespaceDeclarations = isRoot ? MakeFnNsDecl(fnNs) : ImmutableArray<NamespaceBinding>.Empty,
            _stringValue = textValue
        };
        builder.RegisterNode(elem);
        return elem;
    }

    private static XdmElement CreateNullElement(string? key, INodeBuilder builder, NamespaceId fnNs, bool isRoot = false)
    {
        var elemId = builder.AllocateId();
        var attrs = new List<NodeId>();

        if (key != null)
            AddKeyAttribute(elemId, key, attrs, builder);

        var elem = new XdmElement
        {
            Id = elemId,
            Document = default,
            Namespace = fnNs,
            LocalName = "null",
            Prefix = null,
            Attributes = attrs,
            Children = ImmutableArray<NodeId>.Empty,
            NamespaceDeclarations = isRoot ? MakeFnNsDecl(fnNs) : ImmutableArray<NamespaceBinding>.Empty
        };
        builder.RegisterNode(elem);
        return elem;
    }

    private static void AddKeyAttribute(NodeId parentId, string key, List<NodeId> attrs, INodeBuilder builder)
    {
        AddAttribute(parentId, NamespaceId.None, "key", key, attrs, builder);
    }

    private static void AddAttribute(NodeId parentId, NamespaceId ns, string localName, string value, List<NodeId> attrs, INodeBuilder builder)
    {
        var attrId = builder.AllocateId();
        var attr = new XdmAttribute
        {
            Id = attrId,
            Document = default,
            Namespace = ns,
            LocalName = localName,
            Value = value,
            Parent = parentId
        };
        builder.RegisterNode(attr);
        attrs.Add(attrId);
    }
}
