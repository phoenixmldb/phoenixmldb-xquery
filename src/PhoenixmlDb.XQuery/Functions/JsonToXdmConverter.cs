using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

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
                var map = new Execution.OrderedXdmMap(XdmMapKeyComparer.Instance);
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
