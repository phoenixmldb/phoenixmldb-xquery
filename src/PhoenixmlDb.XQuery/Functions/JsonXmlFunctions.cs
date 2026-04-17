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
        // Validate namespace: elements must be in the fn namespace
        // (http://www.w3.org/2005/xpath-functions)
        var elemNsUri = store.GetNamespaceUri(elem.Namespace);
        if (string.IsNullOrEmpty(elemNsUri) && elem.Namespace == NamespaceId.None && !string.IsNullOrEmpty(elem.Prefix))
        {
            // Try resolving from element's own namespace declarations
            foreach (var nsDecl in elem.NamespaceDeclarations)
            {
                if (nsDecl.Prefix == elem.Prefix)
                {
                    elemNsUri = store.GetNamespaceUri(nsDecl.Namespace);
                    break;
                }
            }
        }
        if (!string.IsNullOrEmpty(elemNsUri) && elemNsUri != "http://www.w3.org/2005/xpath-functions")
            throw new XQueryException("FOJS0006", $"Element '{elem.LocalName}' is in namespace '{elemNsUri}', expected 'http://www.w3.org/2005/xpath-functions'");

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
                // Parse as xs:double per spec — the text is an xs:double lexical representation
                if (!double.TryParse(text, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var numVal))
                    throw new XQueryException("FOJS0006", $"Invalid number value: '{text}'");
                if (double.IsNaN(numVal) || double.IsInfinity(numVal))
                    throw new XQueryException("FOJS0006", $"Invalid JSON number: '{text}' (NaN/Infinity not allowed)");
                // Serialize as a valid JSON number using XPath double-to-string rules
                sb.Append(FormatJsonNumber(numVal));
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
                ValidateAttributes(elem, store, "map", ["key", "escaped-key", "escaped"]);
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
    /// Formats a double value as a valid JSON number per the XPath double-to-string specification.
    /// Uses the XPath casting rules: fixed notation for |v| in [0.000001, 1000000), scientific otherwise.
    /// </summary>
    internal static string FormatJsonNumber(double value)
    {
        if (value == 0.0)
            return double.IsNegative(value) ? "-0" : "0";

        var abs = Math.Abs(value);
        string s;
        if (abs >= 0.000001 && abs < 1000000)
        {
            // Fixed notation — use G17 for precision then strip trailing zeros after decimal point
            s = value.ToString("R", CultureInfo.InvariantCulture);
            // "R" may produce scientific notation for some values; force fixed if so
            if (s.Contains('E', StringComparison.OrdinalIgnoreCase))
                s = value.ToString("0.#################", CultureInfo.InvariantCulture);
        }
        else
        {
            // Scientific notation: one digit before decimal, strip trailing zeros from mantissa
            s = value.ToString("R", CultureInfo.InvariantCulture);
            if (!s.Contains('E', StringComparison.OrdinalIgnoreCase))
            {
                // Force scientific notation
                s = value.ToString("0.0################E+0", CultureInfo.InvariantCulture);
            }
            else
            {
                // Normalize: ensure at least one decimal digit in mantissa (e.g., 1E6 → 1.0E6)
                var eIdx = s.IndexOf('E', StringComparison.OrdinalIgnoreCase);
                var mantissa = s[..eIdx];
                var exponent = s[eIdx..];
                if (!mantissa.Contains('.'))
                    mantissa += ".0";
                s = mantissa + exponent;
            }
            // Remove '+' from exponent (1.0E+6 → 1.0E6) per JSON/XPath conventions
            s = s.Replace("E+", "E");
        }
        return s;
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
                case '/':
                    sb.Append("\\/");
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
                    if (c < 0x20 || c == 0x7F)
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
                    if (c < 0x20 || c == 0x7F)
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
                    if (c < 0x20 || c == 0x7F)
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
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

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
        bool indent = false;
        var options = arguments[1];
        if (options is Dictionary<object, object?> map)
        {
            if (map.TryGetValue("indent", out var indentVal))
            {
                if (indentVal is bool ib)
                    indent = ib;
                else
                    throw new XQueryException("XPTY0004", "Option 'indent' must be a boolean value");
            }
            if (map.TryGetValue("validate", out var validateVal))
            {
                if (validateVal is not bool)
                    throw new XQueryException("XPTY0004", "Option 'validate' must be a boolean value");
                if (validateVal is true)
                    throw new XQueryException("FOJS0004", "Option 'validate' is true but the processor is not schema-aware");
            }
        }

        var input = arguments[0];
        if (input is null)
            return ValueTask.FromResult<object?>(null);

        var store = context.NodeStore;
        if (store is null)
            throw new XQueryException("FOJS0006", "xml-to-json requires a node store");

        var elem = XmlToJsonFunction.ResolveToElement(input, store);
        if (elem is null)
            return ValueTask.FromResult<object?>(null);

        var sb = new StringBuilder();
        XmlToJsonFunction.SerializeJsonElement(elem, store, sb);
        var result = sb.ToString();

        if (indent)
        {
            // Re-parse and pretty-print the JSON
            try
            {
                using var doc = JsonDocument.Parse(result);
                result = JsonSerializer.Serialize(doc.RootElement, IndentedJsonOptions);
            }
            catch { /* If re-parsing fails, return the unindented version */ }
        }

        return ValueTask.FromResult<object?>(result);
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
            var doc = JsonToXmlConverter.Convert(jsonText, builder, liberal: false, duplicates: "retain",
                escape: false, fallback: null, baseUri: context.StaticBaseUri);
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
        var duplicates = "retain";
        Func<string, Task<string>>? fallbackFn = null;
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

            // fallback option: must be a function with arity 1
            if (map.TryGetValue("fallback", out var fallbackVal))
            {
                if (fallbackVal is not XQueryFunction fallbackFunc)
                    throw new XQueryException("XPTY0004", "Option 'fallback' must be a function");
                // Validate arity: must accept exactly 1 argument
                if (fallbackFunc.Parameters.Count != 1)
                    throw new XQueryException("XPTY0004", "Option 'fallback' must be a function with arity 1");
                // escape=true + fallback is an error per spec (FOJS0005)
                if (escape)
                    throw new XQueryException("FOJS0005", "The 'fallback' option cannot be used when 'escape' is true");
                var capturedFn = fallbackFunc;
                var capturedCtx = context;
#pragma warning disable CA2008 // Task.ContinueWith without TaskScheduler — result used synchronously by caller
                fallbackFn = s => capturedFn.InvokeAsync(new object?[] { s }, capturedCtx)
                    .AsTask()
                    .ContinueWith(t => t.Result?.ToString() ?? "", TaskScheduler.Default);
#pragma warning restore CA2008
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
            var doc = JsonToXmlConverter.Convert(jsonText, builder, liberal, duplicates, escape,
                fallback: fallbackFn, baseUri: context.StaticBaseUri);
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

    // Characters that are invalid in XML 1.0 (excluding surrogates handled separately)
    // Valid XML 1.0 chars: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
    private static bool IsXml10Invalid(char c) =>
        c < 0x20 && c != 0x09 && c != 0x0A && c != 0x0D;

    public static XdmDocument Convert(
        string json,
        INodeBuilder builder,
        bool liberal = false,
        string duplicates = "use-first",
        bool escape = false,
        Func<string, Task<string>>? fallback = null,
        string? baseUri = null)
    {
        // Strip BOM (U+FEFF) if present — JSON allows BOM per spec but System.Text.Json doesn't
        if (json.Length > 0 && json[0] == '\uFEFF')
            json = json[1..];

        // Pre-process lone surrogates so System.Text.Json can parse the JSON.
        // System.Text.Json rejects lone surrogates (\uD800-\uDFFF not in valid pair).
        // - escape=false, no fallback: replace with \uFFFD (replacement char per spec)
        // - escape=false, with fallback: replace with PUA sentinel (U+E010..E011 encoding) so
        //   ApplyFallbackToString can recover the original codepoint and call fallback(\uXXXX)
        // - escape=true: replace with PUA sentinel so ProcessEscapeTrue can recover the
        //   original \uXXXX sequence and emit it literally in the output
        json = ReplaceLoneSurrogates(json, useSentinel: escape || fallback != null);

        using var jsonDoc = JsonDocument.Parse(json,
            new JsonDocumentOptions { AllowTrailingCommas = liberal, CommentHandling = JsonCommentHandling.Skip });

        // Intern the fn namespace to get the NamespaceId for this builder
        var fnNs = builder.InternNamespace(FnNamespaceUri);

        var docId = builder.AllocateId();
        var rootElem = ConvertValue(jsonDoc.RootElement, null, builder, fnNs, duplicates, isRoot: true, escape: escape, fallback: fallback);

        var doc = new XdmDocument
        {
            Id = docId,
            Document = default,
            Children = new[] { rootElem.Id },
            DocumentElement = rootElem.Id,
            BaseUri = baseUri
        };
        builder.RegisterNode(doc);
        rootElem.Parent = docId;

        return doc;
    }

    private static XdmElement ConvertValue(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, string duplicates, bool isRoot = false, bool escape = false, Func<string, Task<string>>? fallback = null)
    {
        return je.ValueKind switch
        {
            JsonValueKind.Object => ConvertObject(je, key, builder, fnNs, duplicates, isRoot, escape, fallback),
            JsonValueKind.Array => ConvertArray(je, key, builder, fnNs, duplicates, isRoot, escape, fallback),
            JsonValueKind.String => CreateStringElement(je, key, builder, fnNs, isRoot, escape, fallback),
            JsonValueKind.Number => CreateSimpleElement("number", je.GetRawText(), key, builder, fnNs, isRoot),
            JsonValueKind.True => CreateSimpleElement("boolean", "true", key, builder, fnNs, isRoot),
            JsonValueKind.False => CreateSimpleElement("boolean", "false", key, builder, fnNs, isRoot),
            JsonValueKind.Null => CreateNullElement(key, builder, fnNs, isRoot),
            _ => throw new XQueryException("FOJS0001", $"Unsupported JSON value kind: {je.ValueKind}")
        };
    }

    private static IReadOnlyList<NamespaceBinding> MakeFnNsDecl(NamespaceId fnNs) =>
        new[] { new NamespaceBinding("", fnNs) };

    private static XdmElement ConvertObject(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, string duplicates, bool isRoot = false, bool escape = false, Func<string, Task<string>>? fallback = null)
    {
        var elemId = builder.AllocateId();
        var children = new List<NodeId>();
        var attrs = new List<NodeId>();
        var childElems = new List<XdmElement>();

        if (key != null)
            AddKeyAttribute(elemId, key, attrs, builder);

        var seenKeys = duplicates != "retain" ? new HashSet<string>() : null;
        foreach (var prop in je.EnumerateObject())
        {
            // For escape=true, determine the key's effective value and whether escaped-key is needed
            string effectiveKey;
            bool needsEscapedKey = false;
            if (escape)
            {
                (effectiveKey, needsEscapedKey) = ProcessJsonKeyForEscape(prop.Name);
            }
            else
            {
                effectiveKey = prop.Name;
                // For escape=false, check if the decoded key contains XML-invalid chars
                if (fallback != null)
                {
                    effectiveKey = ApplyFallbackToString(prop.Name, fallback);
                }
                else
                {
                    effectiveKey = ReplaceXmlInvalidChars(prop.Name);
                }
            }

            if (seenKeys != null && !seenKeys.Add(effectiveKey))
            {
                // Duplicate key found (after processing)
                if (duplicates == "reject")
                    throw new XQueryException("FOJS0003", $"Duplicate key '{effectiveKey}' in JSON object");
                // use-first: skip subsequent occurrences
                continue;
            }

            var child = ConvertValue(prop.Value, effectiveKey, builder, fnNs, duplicates, escape: escape, fallback: fallback);

            // Add escaped-key="true" if the key had retained escape sequences
            if (needsEscapedKey)
                AddAttribute(child.Id, NamespaceId.None, "escaped-key", "true", child.Attributes as List<NodeId> ?? new List<NodeId>(), builder);

            child.Parent = elemId;
            children.Add(child.Id);
            childElems.Add(child);
        }

        // Compute _stringValue as concatenation of children string values (XPath atomization)
        var stringValue = string.Concat(childElems.Select(c => c._stringValue ?? ""));

        var elem = new XdmElement
        {
            Id = elemId,
            Document = default,
            Namespace = fnNs,
            LocalName = "map",
            Prefix = null,
            Attributes = attrs,
            Children = children,
            NamespaceDeclarations = isRoot ? MakeFnNsDecl(fnNs) : ImmutableArray<NamespaceBinding>.Empty,
            _stringValue = stringValue
        };
        builder.RegisterNode(elem);
        return elem;
    }

    private static XdmElement ConvertArray(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, string duplicates, bool isRoot = false, bool escape = false, Func<string, Task<string>>? fallback = null)
    {
        var elemId = builder.AllocateId();
        var children = new List<NodeId>();
        var attrs = new List<NodeId>();
        var childElems = new List<XdmElement>();

        if (key != null)
            AddKeyAttribute(elemId, key, attrs, builder);

        foreach (var item in je.EnumerateArray())
        {
            var child = ConvertValue(item, null, builder, fnNs, duplicates, escape: escape, fallback: fallback);
            child.Parent = elemId;
            children.Add(child.Id);
            childElems.Add(child);
        }

        // Compute _stringValue as concatenation of children string values (XPath atomization)
        var stringValue = string.Concat(childElems.Select(c => c._stringValue ?? ""));

        var elem = new XdmElement
        {
            Id = elemId,
            Document = default,
            Namespace = fnNs,
            LocalName = "array",
            Prefix = null,
            Attributes = attrs,
            Children = children,
            NamespaceDeclarations = isRoot ? MakeFnNsDecl(fnNs) : ImmutableArray<NamespaceBinding>.Empty,
            _stringValue = stringValue
        };
        builder.RegisterNode(elem);
        return elem;
    }

    /// <summary>
    /// Creates a fn:string element.
    /// - escape=false: text = decoded value with XML-invalid chars replaced (or fallback called).
    /// - escape=true: text = raw JSON content with only \\" and \\/ decoded; add escaped="true" if has retained sequences.
    /// </summary>
    private static XdmElement CreateStringElement(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, bool isRoot, bool escape, Func<string, Task<string>>? fallback)
    {
        if (escape)
        {
            // Extract raw JSON string content (between the outer quotes)
            var raw = je.GetRawText();
            var inner = raw.Length >= 2 ? raw.Substring(1, raw.Length - 2) : "";

            // Process: decode only \\" → " and \\/ → /, keep everything else
            (var textValue, var hasRetainedEscapes) = ProcessEscapeTrue(inner);

            var elem = CreateSimpleElement("string", textValue, key, builder, fnNs, isRoot);
            if (hasRetainedEscapes)
                AddAttribute(elem.Id, NamespaceId.None, "escaped", "true", elem.Attributes as List<NodeId> ?? new List<NodeId>(), builder);
            return elem;
        }
        else
        {
            // escape=false: use the decoded string
            var decoded = je.GetString() ?? "";

            // Apply fallback or replace XML-invalid characters
            string textValue;
            if (fallback != null)
                textValue = ApplyFallbackToString(decoded, fallback);
            else
                textValue = ReplaceXmlInvalidChars(decoded);

            return CreateSimpleElement("string", textValue, key, builder, fnNs, isRoot);
        }
    }

    /// <summary>
    /// Process a raw JSON string inner content (without outer quotes) for escape=true mode.
    /// Returns (processed text, hasRetainedEscapes).
    /// Only \" → " and \/ → / are decoded; all other sequences (\\, \r, \t, \n, \b, \f, \uXXXX) are kept.
    /// </summary>
    private static (string text, bool hasEscapes) ProcessEscapeTrue(string inner)
    {
        if (!inner.Contains('\\'))
            return (inner, false);

        var sb = new StringBuilder(inner.Length);
        bool hasRetained = false;
        int i = 0;
        while (i < inner.Length)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                var next = inner[i + 1];
                if (next == '"')
                {
                    // \" → " (decoded, not retained)
                    sb.Append('"');
                    i += 2;
                }
                else if (next == '/')
                {
                    // \/ → / (decoded, not retained)
                    sb.Append('/');
                    i += 2;
                }
                else if (next == 'u' && i + 5 < inner.Length)
                {
                    // Check for PUA sentinel: \uE010 + 4×\uE0XY + \uE011 (36 chars total)
                    // This encodes a lone surrogate that was pre-replaced to allow JSON parsing.
                    // We recover and emit the original \uXXXX escape sequence.
                    if (i + 35 < inner.Length
                        && inner[i + 2] == 'E' && (inner[i + 3] == '0') && inner[i + 4] == '1' && inner[i + 5] == '0'
                        && TryDecodeSentinelEscapeTrue(inner, i + 6, out var originalHex))
                    {
                        sb.Append("\\u").Append(originalHex);
                        hasRetained = true;
                        i += 36; // 6 sequences × 6 chars each
                    }
                    else
                    {
                        // \uXXXX — decode and decide whether to retain or decode
                        var hexSpan = inner.AsSpan(i + 2, 4);
                        if (ushort.TryParse(hexSpan, System.Globalization.NumberStyles.HexNumber, null, out var cp))
                        {
                            // Check for surrogate pair: \uD800-\uDBFF followed by \uDC00-\uDFFF
                            if (char.IsHighSurrogate((char)cp) && i + 11 < inner.Length
                                && inner[i + 6] == '\\' && inner[i + 7] == 'u')
                            {
                                var lowHex = inner.AsSpan(i + 8, 4);
                                if (ushort.TryParse(lowHex, System.Globalization.NumberStyles.HexNumber, null, out var lowCp)
                                    && char.IsLowSurrogate((char)lowCp))
                                {
                                    // Valid surrogate pair — decode to the actual character (always XML-valid)
                                    sb.Append((char)cp).Append((char)lowCp);
                                    i += 12;
                                    continue;
                                }
                            }

                            // Short escape forms for common control chars
                            if (cp == 0x08) { sb.Append("\\b"); hasRetained = true; i += 6; }
                            else if (cp == 0x09) { sb.Append("\\t"); hasRetained = true; i += 6; }
                            else if (cp == 0x0A) { sb.Append("\\n"); hasRetained = true; i += 6; }
                            else if (cp == 0x0C) { sb.Append("\\f"); hasRetained = true; i += 6; }
                            else if (cp == 0x0D) { sb.Append("\\r"); hasRetained = true; i += 6; }
                            else if (IsXml10Invalid((char)cp) || char.IsHighSurrogate((char)cp) || char.IsLowSurrogate((char)cp))
                            {
                                // XML-invalid char or lone surrogate — retain as \uXXXX
                                sb.Append('\\').Append('u').Append(inner, i + 2, 4);
                                hasRetained = true;
                                i += 6;
                            }
                            else
                            {
                                // XML-valid character — decode to literal
                                sb.Append((char)cp);
                                i += 6;
                            }
                        }
                        else
                        {
                            // Invalid hex — retain as-is
                            sb.Append('\\').Append('u').Append(inner, i + 2, 4);
                            hasRetained = true;
                            i += 6;
                        }
                    }
                }
                else
                {
                    // \\, \r, \t, \n, \b, \f — retain as-is
                    sb.Append('\\').Append(next);
                    hasRetained = true;
                    i += 2;
                }
            }
            else
            {
                sb.Append(inner[i]);
                i++;
            }
        }
        return (sb.ToString(), hasRetained);
    }

    /// <summary>
    /// Checks if the 30 chars starting at 'pos' in 'inner' form 4 sentinel-digit \uXXXX sequences
    /// followed by \uE011 (6 chars). If so, decodes to the original 4-char hex string.
    /// The full sentinel is: \uE010 (already consumed) + 4×\uE0XY + \uE011 = 5×6 = 30 chars from pos.
    /// </summary>
    private static bool TryDecodeSentinelEscapeTrue(string inner, int pos, out string originalHex)
    {
        // 4 sentinel digit sequences + \uE011 = 5 sequences × 6 chars = 30 chars
        if (pos + 30 > inner.Length)
        {
            originalHex = "";
            return false;
        }

        var hexChars = new char[4];
        for (int k = 0; k < 4; k++)
        {
            int offset = pos + k * 6;
            // Each must be \uE00X where X is 0-F (E000..E00F = sentinel digits 0..F)
            // \uE000 = '0', \uE001 = '1', ..., \uE009 = '9', \uE00A = 'A', ..., \uE00F = 'F'
            if (inner[offset] != '\\' || inner[offset + 1] != 'u'
                || inner[offset + 2] != 'E' || inner[offset + 3] != '0'
                || inner[offset + 4] != '0')
            {
                originalHex = "";
                return false;
            }
            var digitChar = inner[offset + 5]; // '0'-'9' or 'A'-'F'
            if (!IsHexDigit(digitChar))
            {
                originalHex = "";
                return false;
            }
            hexChars[k] = char.ToUpperInvariant(digitChar);
        }

        // Last sequence must be \uE011
        int endOffset = pos + 4 * 6;
        if (inner[endOffset] != '\\' || inner[endOffset + 1] != 'u'
            || inner[endOffset + 2] != 'E' || inner[endOffset + 3] != '0'
            || inner[endOffset + 4] != '1' || inner[endOffset + 5] != '1')
        {
            originalHex = "";
            return false;
        }

        originalHex = new string(hexChars);
        return true;
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

    /// <summary>
    /// For escape=true mode: determine what key value and escaped-key flag to use.
    /// The key is the decoded property name from System.Text.Json (which already decoded \\uXXXX).
    /// We need the original raw key to determine if it had escape sequences.
    /// Since System.Text.Json gives us only the decoded name, we re-examine by looking for
    /// escape sequences in the raw JSON.
    ///
    /// Strategy: the prop.Name from JsonElement is already decoded. We need to check if the
    /// raw JSON key had any backslash sequences (other than \" and \/).
    /// We use GetRawText equivalent: for object properties, we re-parse the raw JSON key
    /// from the property's raw representation.
    /// </summary>
    private static (string effectiveKey, bool needsEscapedKey) ProcessJsonKeyForEscape(string decodedKey)
    {
        // System.Text.Json gives us only the decoded key name.
        // For escape=true, the spec says the key attribute value should have kept escape sequences.
        // But since System.Text.Json decoded everything, we can't distinguish.
        // We use decodedKey as-is for the attribute value (per spec: the key is the decoded form,
        // and escaped-key="true" is set if the decoded form differs from what would be written
        // without escaping, i.e., if any character in the key had to be escaped in the original JSON).
        //
        // Actually, the spec says: for escape=true, retain escape sequences in keys too.
        // But the XPath spec says: "key attribute...is set to the key name after decoding JSON escape
        // sequences, except that those described in Rule 1a [backslash+char kept as-is] are retained."
        // Rule 1a: \\ \b \f \n \r \t \uXXXX are retained; only \" and \/ are decoded.
        //
        // Problem: we only have the fully-decoded key. We have no way to re-construct \uXXXX
        // representations from the decoded characters.
        //
        // However, for test 019 ({"a\\":3}), the JSON property name raw is "a\\" → decoded is "a\".
        // The expected key attribute value is "a\\" with escaped-key="true".
        // So the key should be the RAW form (with retained escapes).
        //
        // Since System.Text.Json doesn't give us the raw key, we cannot do this correctly here.
        // The caller must pass the raw key. But System.Text.Json property enumeration only gives decoded.
        //
        // For now: we detect if the decoded key contains any char that would have been escaped.
        // A backslash in the decoded key means the original had \\, which is retained → escaped-key.
        // Any \b\f\n\r\t control chars can't be detected (they were decoded by System.Text.Json).
        // \uXXXX sequences: non-BMP chars or control chars from \uXXXX.
        //
        // This is a fundamental limitation. The best we can do is check if decoded key has a backslash
        // (meaning original had \\) or control chars < 0x20 that would have been \uXXXX.

        // For keys decoded from JSON, rebuild escaped form for escape=true:
        // If decoded key has backslash → original had \\ → rebuild as \\
        // If decoded key has chars < 0x20 (control chars) → original had \uXXXX → rebuild as \uXXXX
        // If decoded key has PUA sentinel sequence (lone surrogate pre-encoded) → rebuild as \uXXXX
        var needsEscaping = false;
        var sb = new StringBuilder(decodedKey.Length);
        int ki = 0;
        while (ki < decodedKey.Length)
        {
            var c = decodedKey[ki];

            // Check for PUA sentinel: SentinelStart (U+E010) + 4 sentinel digits + SentinelEnd (U+E011)
            if (c == SentinelStart && ki + 5 < decodedKey.Length && decodedKey[ki + 5] == SentinelEnd
                && IsSentinelDigit(decodedKey[ki + 1]) && IsSentinelDigit(decodedKey[ki + 2])
                && IsSentinelDigit(decodedKey[ki + 3]) && IsSentinelDigit(decodedKey[ki + 4]))
            {
                var hex = string.Concat(
                    SentinelToHexDigit(decodedKey[ki + 1]),
                    SentinelToHexDigit(decodedKey[ki + 2]),
                    SentinelToHexDigit(decodedKey[ki + 3]),
                    SentinelToHexDigit(decodedKey[ki + 4]));
                sb.Append($"\\u{hex}");
                needsEscaping = true;
                ki += 6;
            }
            else if (c == '\\')
            {
                sb.Append("\\\\");
                needsEscaping = true;
                ki++;
            }
            else if (c == '\b')
            {
                sb.Append("\\b");
                needsEscaping = true;
                ki++;
            }
            else if (c == '\f')
            {
                sb.Append("\\f");
                needsEscaping = true;
                ki++;
            }
            else if (c == '\n')
            {
                sb.Append("\\n");
                needsEscaping = true;
                ki++;
            }
            else if (c == '\r')
            {
                sb.Append("\\r");
                needsEscaping = true;
                ki++;
            }
            else if (c == '\t')
            {
                sb.Append("\\t");
                needsEscaping = true;
                ki++;
            }
            else if (c < 0x20)
            {
                sb.Append($"\\u{(int)c:X4}");
                needsEscaping = true;
                ki++;
            }
            else if (char.IsHighSurrogate(c) || char.IsLowSurrogate(c))
            {
                // Lone surrogate in escape=true → keep as \uXXXX
                sb.Append($"\\u{(int)c:X4}");
                needsEscaping = true;
                ki++;
            }
            else
            {
                sb.Append(c);
                ki++;
            }
        }
        return needsEscaping ? (sb.ToString(), true) : (decodedKey, false);
    }

    /// <summary>
    /// Apply fallback function to each invalid character in the string.
    /// The fallback is called with the original escape sequence (e.g. "\uDEAD") or the invalid char's
    /// \uXXXX form (e.g. "\u000C" for form-feed).
    /// Lone surrogates appear as PUA sentinel sequences (U+E000 + 4×encoded-digit + U+E011)
    /// because ReplaceLoneSurrogates was called with useSentinel=true.
    /// </summary>
    private static string ApplyFallbackToString(string value, Func<string, Task<string>> fallback)
    {
        // Check if string has any chars requiring fallback
        bool hasInvalid = false;
        foreach (var c in value)
        {
            if (IsXml10Invalid(c) || char.IsHighSurrogate(c) || char.IsLowSurrogate(c) || c == SentinelStart)
            {
                hasInvalid = true;
                break;
            }
        }
        if (!hasInvalid)
            return value;

        var sb = new StringBuilder(value.Length);
        int i = 0;
        while (i < value.Length)
        {
            var c = value[i];

            // Check for PUA sentinel sequence: U+E000 + 4 PUA encoded digits + U+E011
            // This was a lone surrogate encoded by AppendSurrogateAsSentinel.
            if (c == SentinelStart && i + 5 < value.Length && value[i + 5] == SentinelEnd
                && IsSentinelDigit(value[i + 1]) && IsSentinelDigit(value[i + 2])
                && IsSentinelDigit(value[i + 3]) && IsSentinelDigit(value[i + 4]))
            {
                var hexDigits = string.Concat(
                    SentinelToHexDigit(value[i + 1]),
                    SentinelToHexDigit(value[i + 2]),
                    SentinelToHexDigit(value[i + 3]),
                    SentinelToHexDigit(value[i + 4]));
                var escaped = $"\\u{hexDigits}";
                var fb = fallback(escaped).GetAwaiter().GetResult();
                sb.Append(fb);
                i += 6;
            }
            else if (char.IsHighSurrogate(c) || char.IsLowSurrogate(c))
            {
                // Surrogate that appeared literally (not via \uXXXX escape)
                var escaped = $"\\u{(int)c:X4}";
                var fb = fallback(escaped).GetAwaiter().GetResult();
                sb.Append(fb);
                i++;
            }
            else if (IsXml10Invalid(c))
            {
                // XML 1.0 invalid char (e.g. U+000C) decoded from \uXXXX by System.Text.Json
                var escaped = $"\\u{(int)c:X4}";
                var fb = fallback(escaped).GetAwaiter().GetResult();
                sb.Append(fb);
                i++;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool IsSentinelDigit(char c) => c >= '\uE000' && c <= '\uE00F';
    private static char SentinelToHexDigit(char c)
    {
        var n = c - '\uE000';
        return n < 10 ? (char)('0' + n) : (char)('A' + n - 10);
    }

    /// <summary>
    /// Replace characters that are invalid in XML 1.0 with U+FFFD.
    /// Called when escape=false and no fallback is provided.
    /// </summary>
    private static string ReplaceXmlInvalidChars(string value)
    {
        if (!value.Any(IsXml10Invalid))
            return value;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(IsXml10Invalid(c) ? '\uFFFD' : c);
        return sb.ToString();
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

    // PUA sentinels for encoding lone surrogates when fallback is provided.
    // We use Private Use Area chars to encode the original 4 hex digits so the fallback can be
    // called with the correct \uXXXX escape sequence.
    // U+E010 = sentinel start marker (NOT in the hex-digit range)
    // U+E000-U+E00F = hex digit encoding: digit 0-9,A-F → U+E000-U+E00F
    // U+E011 = sentinel end marker
    // A lone surrogate \uXXXX becomes: U+E010 + 4×(U+E000..E00F) + U+E011 (6 chars total)
    private const char SentinelStart = '\uE010';
    private const char SentinelEnd = '\uE011';
    private static char SentinelHexDigit(char hexChar) =>
        (char)(0xE000 + (hexChar >= 'a' ? hexChar - 'a' + 10 : hexChar >= 'A' ? hexChar - 'A' + 10 : hexChar - '0'));

    /// <summary>
    /// Replaces lone surrogate escape sequences (\uDxxx without a valid pair) in a JSON string
    /// so System.Text.Json can parse it. Called for escape=false mode only.
    /// When useSentinel=false: replace with \uFFFD (replacement character, no fallback).
    /// When useSentinel=true: replace with a PUA-encoded sentinel sequence so the original
    /// codepoint can be recovered when calling the fallback function.
    /// </summary>
    private static string ReplaceLoneSurrogates(string json, bool useSentinel = false)
    {
        // Quick scan: does the JSON contain any \u escape at all?
        if (!json.Contains("\\u", StringComparison.Ordinal))
            return json;

        var sb = new StringBuilder(json.Length);
        for (int i = 0; i < json.Length; i++)
        {
            if (i + 5 <= json.Length && json[i] == '\\' && json[i + 1] == 'u')
            {
                if (ushort.TryParse(json.AsSpan(i + 2, 4), NumberStyles.HexNumber, null, out var codeUnit))
                {
                    if (char.IsHighSurrogate((char)codeUnit))
                    {
                        // Check for following low surrogate
                        if (i + 11 <= json.Length && json[i + 6] == '\\' && json[i + 7] == 'u'
                            && ushort.TryParse(json.AsSpan(i + 8, 4), NumberStyles.HexNumber, null, out var low)
                            && char.IsLowSurrogate((char)low))
                        {
                            // Valid surrogate pair — keep as-is
                            sb.Append(json, i, 12);
                            i += 11;
                            continue;
                        }
                        // Lone high surrogate
                        if (useSentinel)
                            AppendSurrogateAsSentinel(sb, json.AsSpan(i + 2, 4));
                        else
                            sb.Append("\\uFFFD");
                        i += 5;
                        continue;
                    }
                    else if (char.IsLowSurrogate((char)codeUnit))
                    {
                        // Lone low surrogate
                        if (useSentinel)
                            AppendSurrogateAsSentinel(sb, json.AsSpan(i + 2, 4));
                        else
                            sb.Append("\\uFFFD");
                        i += 5;
                        continue;
                    }
                }
            }
            sb.Append(json[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Appends a PUA-encoded surrogate sentinel to the JSON replacement.
    /// The sentinel is a JSON escape sequence that represents 6 chars:
    /// U+E000, encoded-digit-1, encoded-digit-2, encoded-digit-3, encoded-digit-4, U+E011
    /// which after JSON parsing becomes the 6-char sentinel in the decoded string.
    /// </summary>
    private static void AppendSurrogateAsSentinel(StringBuilder sb, ReadOnlySpan<char> fourHexDigits)
    {
        // We need to emit these as JSON \uXXXX sequences so System.Text.Json will decode them correctly.
        // Each of the 4 hex digits of the original surrogate codepoint is encoded as a PUA char:
        // digit 0..F → U+E000..U+E00F. Wrapped in U+E000 (start) and U+E011 (end) sentinels.
        sb.Append("\\uE010"); // sentinel start
        foreach (var c in fourHexDigits)
        {
            var puaChar = (int)SentinelHexDigit(c);
            sb.Append($"\\u{puaChar:X4}"); // each digit as PUA char (E000..E00F)
        }
        sb.Append("\\uE011"); // sentinel end
    }
}
