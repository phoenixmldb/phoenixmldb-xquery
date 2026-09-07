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
            throw context.Error("XPTY0004", "A sequence of " + arr.Length + " items is not allowed as the first argument of xml-to-json()");

        var store = context.NodeStore;
        if (store is null)
            throw context.Error("FOJS0006", "xml-to-json requires a node store");

        var elem = ResolveToElement(input, store, context);
        if (elem is null)
            return ValueTask.FromResult<object?>(null);

        var sb = new StringBuilder();
        SerializeJsonElement(elem, store, sb, context);
        return ValueTask.FromResult<object?>(sb.ToString());
    }

    internal static XdmElement? ResolveToElement(object? input, INodeStore store, Ast.ExecutionContext? context = null)
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
                        throw context.Error("FOJS0006", "xml-to-json input contains multiple element children");
                    first = child;
                }
            }
            return first;
        }
        return null;
    }

    internal static void SerializeJsonElement(XdmElement elem, INodeStore store, StringBuilder sb, Ast.ExecutionContext? context = null)
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
            throw context.Error("FOJS0006", $"Element '{elem.LocalName}' is in namespace '{elemNsUri}', expected 'http://www.w3.org/2005/xpath-functions'");

        var localName = elem.LocalName;

        switch (localName)
        {
            case "null":
            {
                // Null must have no non-whitespace text content and no element children
                ValidateNoElementChildren(elem, store, "null", context);
                var nullText = GetTextContent(elem, store, context).Trim();
                if (nullText.Length > 0)
                    throw context.Error("FOJS0006", "null element must have no content");
                ValidateAttributes(elem, store, "null", ["key", "escaped-key"], context);
                sb.Append("null");
                break;
            }

            case "boolean":
            {
                ValidateNoElementChildren(elem, store, "boolean", context);
                ValidateAttributes(elem, store, "boolean", ["key", "escaped-key"], context);
                var text = GetTextContent(elem, store, context).Trim();
                // Accepts "true", "false", "1", "0"
                sb.Append(text switch
                {
                    "true" or "1" => "true",
                    "false" or "0" => "false",
                    _ => throw context.Error("FOJS0006", $"Invalid boolean value: '{text}'")
                });
                break;
            }

            case "number":
            {
                ValidateNoElementChildren(elem, store, "number", context);
                ValidateAttributes(elem, store, "number", ["key", "escaped-key"], context);
                var text = GetTextContent(elem, store, context).Trim();
                // Parse as xs:double per spec — the text is an xs:double lexical representation
                if (!double.TryParse(text, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var numVal))
                    throw context.Error("FOJS0006", $"Invalid number value: '{text}'");
                if (double.IsNaN(numVal) || double.IsInfinity(numVal))
                    throw context.Error("FOJS0006", $"Invalid JSON number: '{text}' (NaN/Infinity not allowed)");
                // Serialize as a valid JSON number using XPath double-to-string rules
                sb.Append(FormatJsonNumber(numVal, context));
                break;
            }

            case "string":
            {
                // String elements must contain only text nodes (no element children)
                ValidateNoElementChildren(elem, store, "string", context);
                var escaped = GetAttributeValue(elem, "escaped", store, context);
                ValidateBooleanAttribute(escaped, "escaped", context);
                ValidateAttributes(elem, store, "string", ["key", "escaped-key", "escaped"], context);
                var isEscaped = IsTruthy(escaped, context);
                var text = GetTextContent(elem, store, context);

                sb.Append('"');
                if (isEscaped)
                    AppendValidatedEscapedJsonString(text, sb, context);
                else
                    AppendJsonString(text, sb, context);
                sb.Append('"');
                break;
            }

            case "array":
            {
                ValidateAttributes(elem, store, "array", ["key", "escaped-key"], context);
                // Array must not contain non-whitespace text
                ValidateNoSignificantText(elem, store, "array", context);
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
                        SerializeJsonElement(childElem, store, sb, context);
                    }
                    else if (child is XdmElement childElem2)
                    {
                        // Element not in fn namespace — try processing anyway
                        // (namespace IDs may differ in RTF node stores)
                        if (!first)
                            sb.Append(',');
                        first = false;
                        SerializeJsonElement(childElem2, store, sb, context);
                    }
                }
                sb.Append(']');
                break;
            }

            case "map":
            {
                ValidateAttributes(elem, store, "map", ["key", "escaped-key", "escaped"], context);
                // Map must not contain non-whitespace text
                ValidateNoSignificantText(elem, store, "map", context);
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
                        var key = GetAttributeValue(childElem, "key", store, context);
                        if (key is null)
                            throw context.Error("FOJS0006", "Map entry missing 'key' attribute");

                        var escapedKey = GetAttributeValue(childElem, "escaped-key", store, context);
                        ValidateBooleanAttribute(escapedKey, "escaped-key", context);
                        var isEscapedKey = IsTruthy(escapedKey, context);

                        // Duplicate detection uses decoded key values
                        var decodedKey = DecodeJsonKey(key, isEscapedKey, context);
                        if (!seenKeys.Add(decodedKey))
                            throw context.Error("FOJS0006", $"Duplicate key in map: '{key}'");

                        sb.Append('"');
                        if (isEscapedKey)
                            AppendValidatedEscapedJsonString(key, sb, context);
                        else
                            AppendJsonString(key, sb, context);
                        sb.Append('"');
                        sb.Append(':');
                        SerializeJsonElement(childElem, store, sb, context);
                    }
                    else if (child is XdmElement childElem2)
                    {
                        // Element not in fn namespace — try processing anyway
                        // (namespace IDs may differ in RTF node stores)
                        if (!first)
                            sb.Append(',');
                        first = false;

                        var key2 = GetAttributeValue(childElem2, "key", store, context);
                        if (key2 is null)
                            throw context.Error("FOJS0006", "Map entry missing 'key' attribute");

                        var escapedKey2 = GetAttributeValue(childElem2, "escaped-key", store, context);
                        ValidateBooleanAttribute(escapedKey2, "escaped-key", context);
                        var isEscapedKey2 = IsTruthy(escapedKey2, context);

                        var decodedKey2 = DecodeJsonKey(key2, isEscapedKey2, context);
                        if (!seenKeys.Add(decodedKey2))
                            throw context.Error("FOJS0006", $"Duplicate key in map: '{key2}'");

                        sb.Append('"');
                        if (isEscapedKey2)
                            AppendValidatedEscapedJsonString(key2, sb, context);
                        else
                            AppendJsonString(key2, sb, context);
                        sb.Append('"');
                        sb.Append(':');
                        SerializeJsonElement(childElem2, store, sb, context);
                    }
                }
                sb.Append('}');
                break;
            }

            default:
                throw context.Error("FOJS0006", $"Unknown JSON element type: '{localName}'");
        }
    }

    /// <summary>
    /// Gets the concatenated text content of an element (ignoring comments and PIs).
    /// </summary>
    internal static string GetTextContent(XdmElement elem, INodeStore store, Ast.ExecutionContext? context = null)
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
    internal static string FormatJsonNumber(double value, Ast.ExecutionContext? context = null)
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
    internal static string? GetAttributeValue(XdmElement elem, string localName, INodeStore store, Ast.ExecutionContext? context = null)
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
    internal static bool IsTruthy(string? value, Ast.ExecutionContext? context = null)
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
    internal static void AppendJsonString(string text, StringBuilder sb, Ast.ExecutionContext? context = null)
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
    internal static void AppendEscapedJsonString(string text, StringBuilder sb, Ast.ExecutionContext? context = null)
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
    internal static bool IsValidJsonNumber(string text, Ast.ExecutionContext? context = null)
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
    internal static void ValidateNoElementChildren(XdmElement elem, INodeStore store, string type, Ast.ExecutionContext? context = null)
    {
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmElement)
                throw context.Error("FOJS0006", $"{type} element must not contain child elements");
        }
    }

    /// <summary>
    /// Validates that a container element (array/map) has no non-whitespace text content.
    /// </summary>
    internal static void ValidateNoSignificantText(XdmElement elem, INodeStore store, string type, Ast.ExecutionContext? context = null)
    {
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmText text && text.Value.AsSpan().Trim().Length > 0)
                throw context.Error("FOJS0006", $"{type} element must not contain text content");
        }
    }

    /// <summary>
    /// Validates that only allowed attributes are present on an element.
    /// </summary>
    internal static void ValidateAttributes(XdmElement elem, INodeStore store, string type, string[] allowed, Ast.ExecutionContext? context = null)
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
                    throw context.Error("FOJS0006", $"Invalid attribute '{attr.LocalName}' on {type} element");
            }
            else
            {
                // Check if this is an attribute in the fn namespace — reject unknown ones
                var nsUri = store.GetNamespaceUri(attr.Namespace);
                if (nsUri == "http://www.w3.org/2005/xpath-functions")
                    throw context.Error("FOJS0006", $"Invalid attribute in fn namespace '{attr.LocalName}' on {type} element");
            }
        }
    }

    /// <summary>
    /// Validates that a boolean-like attribute has value "true", "false", "1", or "0".
    /// </summary>
    internal static void ValidateBooleanAttribute(string? value, string attrName, Ast.ExecutionContext? context = null)
    {
        if (value is null)
            return;
        var trimmed = value.Trim();
        if (trimmed != "true" && trimmed != "false" && trimmed != "1" && trimmed != "0")
            throw context.Error("FOJS0006", $"Invalid value '{value}' for attribute '{attrName}'");
    }

    /// <summary>
    /// Like AppendEscapedJsonString but validates that escape sequences are valid JSON.
    /// Throws FOJS0006 for invalid escape sequences.
    /// </summary>
    internal static void AppendValidatedEscapedJsonString(string text, StringBuilder sb, Ast.ExecutionContext? context = null)
    {
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\')
            {
                if (i + 1 >= text.Length)
                    throw context.Error("FOJS0006", "Incomplete escape sequence at end of string");
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
                            throw context.Error("FOJS0006", "Incomplete \\u escape sequence");
                        // Validate hex digits
                        for (int j = i + 2; j < i + 6; j++)
                        {
                            if (!IsHexDigit(text[j]))
                                throw context.Error("FOJS0006", $"Invalid \\u escape sequence: '{text.Substring(i, 6)}'");
                        }
                        sb.Append(text, i, 6);
                        i += 5;
                        continue;
                    default:
                        throw context.Error("FOJS0006", $"Invalid escape sequence '\\{next}'");
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
    internal static string DecodeJsonKey(string rawKey, bool isEscaped, Ast.ExecutionContext? context = null)
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
