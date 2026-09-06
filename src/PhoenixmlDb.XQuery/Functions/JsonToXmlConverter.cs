using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

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
        string? baseUri = null,
        Ast.ExecutionContext? context = null)
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
        var rootElem = ConvertValue(jsonDoc.RootElement, null, builder, fnNs, duplicates, isRoot: true, escape: escape, fallback: fallback, context);

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

    private static XdmElement ConvertValue(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, string duplicates, bool isRoot = false, bool escape = false, Func<string, Task<string>>? fallback = null, Ast.ExecutionContext? context = null)
    {
        return je.ValueKind switch
        {
            JsonValueKind.Object => ConvertObject(je, key, builder, fnNs, duplicates, isRoot, escape, fallback, context),
            JsonValueKind.Array => ConvertArray(je, key, builder, fnNs, duplicates, isRoot, escape, fallback, context),
            JsonValueKind.String => CreateStringElement(je, key, builder, fnNs, isRoot, escape, fallback, context),
            JsonValueKind.Number => CreateSimpleElement("number", je.GetRawText(), key, builder, fnNs, isRoot),
            JsonValueKind.True => CreateSimpleElement("boolean", "true", key, builder, fnNs, isRoot),
            JsonValueKind.False => CreateSimpleElement("boolean", "false", key, builder, fnNs, isRoot),
            JsonValueKind.Null => CreateNullElement(key, builder, fnNs, isRoot),
            _ => throw context.Error("FOJS0001", $"Unsupported JSON value kind: {je.ValueKind}")
        };
    }

    private static IReadOnlyList<NamespaceBinding> MakeFnNsDecl(NamespaceId fnNs) =>
        new[] { new NamespaceBinding("", fnNs) };

    private static XdmElement ConvertObject(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, string duplicates, bool isRoot = false, bool escape = false, Func<string, Task<string>>? fallback = null, Ast.ExecutionContext? context = null)
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
                    throw context.Error("FOJS0003", $"Duplicate key '{effectiveKey}' in JSON object");
                // use-first: skip subsequent occurrences
                continue;
            }

            var child = ConvertValue(prop.Value, effectiveKey, builder, fnNs, duplicates, escape: escape, fallback: fallback, context: context);

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

    private static XdmElement ConvertArray(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, string duplicates, bool isRoot = false, bool escape = false, Func<string, Task<string>>? fallback = null, Ast.ExecutionContext? context = null)
    {
        var elemId = builder.AllocateId();
        var children = new List<NodeId>();
        var attrs = new List<NodeId>();
        var childElems = new List<XdmElement>();

        if (key != null)
            AddKeyAttribute(elemId, key, attrs, builder);

        foreach (var item in je.EnumerateArray())
        {
            var child = ConvertValue(item, null, builder, fnNs, duplicates, escape: escape, fallback: fallback, context: context);
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
    private static XdmElement CreateStringElement(JsonElement je, string? key, INodeBuilder builder, NamespaceId fnNs, bool isRoot, bool escape, Func<string, Task<string>>? fallback, Ast.ExecutionContext? context = null)
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
    private static (string text, bool hasEscapes) ProcessEscapeTrue(string inner, Ast.ExecutionContext? context = null)
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
    private static bool TryDecodeSentinelEscapeTrue(string inner, int pos, out string originalHex, Ast.ExecutionContext? context = null)
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
    private static (string effectiveKey, bool needsEscapedKey) ProcessJsonKeyForEscape(string decodedKey, Ast.ExecutionContext? context = null)
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
    private static string ApplyFallbackToString(string value, Func<string, Task<string>> fallback, Ast.ExecutionContext? context = null)
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
    private static char SentinelToHexDigit(char c, Ast.ExecutionContext? context = null)
    {
        var n = c - '\uE000';
        return n < 10 ? (char)('0' + n) : (char)('A' + n - 10);
    }

    /// <summary>
    /// Replace characters that are invalid in XML 1.0 with U+FFFD.
    /// Called when escape=false and no fallback is provided.
    /// </summary>
    private static string ReplaceXmlInvalidChars(string value, Ast.ExecutionContext? context = null)
    {
        if (!value.Any(IsXml10Invalid))
            return value;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(IsXml10Invalid(c) ? '\uFFFD' : c);
        return sb.ToString();
    }

    private static XdmElement CreateSimpleElement(string localName, string textValue, string? key, INodeBuilder builder, NamespaceId fnNs, bool isRoot = false, Ast.ExecutionContext? context = null)
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

    private static XdmElement CreateNullElement(string? key, INodeBuilder builder, NamespaceId fnNs, bool isRoot = false, Ast.ExecutionContext? context = null)
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

    private static void AddKeyAttribute(NodeId parentId, string key, List<NodeId> attrs, INodeBuilder builder, Ast.ExecutionContext? context = null)
    {
        AddAttribute(parentId, NamespaceId.None, "key", key, attrs, builder);
    }

    private static void AddAttribute(NodeId parentId, NamespaceId ns, string localName, string value, List<NodeId> attrs, INodeBuilder builder, Ast.ExecutionContext? context = null)
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
    private static string ReplaceLoneSurrogates(string json, bool useSentinel = false, Ast.ExecutionContext? context = null)
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
    private static void AppendSurrogateAsSentinel(StringBuilder sb, ReadOnlySpan<char> fourHexDigits, Ast.ExecutionContext? context = null)
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
