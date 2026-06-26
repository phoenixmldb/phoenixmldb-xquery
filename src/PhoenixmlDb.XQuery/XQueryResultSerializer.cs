using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Serializes XQuery result items (nodes, atomic values, arrays, sequences) to strings.
/// </summary>
/// <remarks>
/// <para>
/// This serializer handles all XDM result types produced by the XQuery engine:
/// document and element nodes are serialized as XML, atomic values use their string representation,
/// and sequences/arrays are serialized by concatenating their items.
/// </para>
/// <para>
/// For the simplest usage, call the static <see cref="Serialize(object?, XdmDocumentStore, OutputMethod)"/> method.
/// For repeated serialization with the same store, create an instance and call <see cref="Serialize(object?)"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var store = new XdmDocumentStore();
/// store.LoadFromString("&lt;root&gt;hello&lt;/root&gt;", "urn:input");
///
/// var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
/// await foreach (var item in engine.ExecuteAsync("doc('urn:input')/root"))
/// {
///     string xml = XQueryResultSerializer.Serialize(item, store);
///     Console.WriteLine(xml); // &lt;root&gt;hello&lt;/root&gt;
/// }
/// </code>
/// </example>
public sealed class XQueryResultSerializer
{
    private readonly XdmDocumentStore _store;
    private readonly OutputMethod _method;
    private readonly SerializationOptions _options;

    /// <summary>True while serializing the subtree of a suppress-indentation element via a
    /// nested non-indenting writer, so nested suppressed elements are not re-processed.</summary>
    private bool _inSuppressedSubtree;

    /// <summary>
    /// Creates a new serializer backed by the given document store.
    /// </summary>
    /// <param name="store">The document store used to resolve child nodes and namespaces during serialization.</param>
    /// <param name="method">The output method. Defaults to <see cref="OutputMethod.Adaptive"/>.</param>
    public XQueryResultSerializer(XdmDocumentStore store, OutputMethod method = OutputMethod.Adaptive)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _method = method;
        _options = new SerializationOptions { Method = method };
    }

    /// <summary>
    /// Creates a new serializer backed by the given document store with full serialization options.
    /// </summary>
    /// <param name="store">The document store used to resolve child nodes and namespaces during serialization.</param>
    /// <param name="options">The serialization options.</param>
    public XQueryResultSerializer(XdmDocumentStore store, SerializationOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? SerializationOptions.Default;
        _method = _options.Method;
    }

    /// <summary>
    /// Serializes a single result item to a string.
    /// </summary>
    /// <param name="item">The XQuery result item (node, atomic value, array, sequence, or <c>null</c>).</param>
    /// <returns>The serialized string representation.</returns>
    public string Serialize(object? item)
    {
        var targetEncoding = _options.Encoding != null
            ? System.Text.Encoding.GetEncoding(_options.Encoding)
            : System.Text.Encoding.UTF8;
        using var sw = new Utf8StringWriter(targetEncoding);
        SerializeTo(item, sw);
        var result = sw.ToString();

        // Normalize self-closing tags: .NET's XmlWriter emits "<elem />" but the spec expects "<elem/>".
        // The XHTML output method deliberately keeps the space-before-slash on void elements
        // (HTML-compatibility), so this normalization is skipped for it.
        if (_method != OutputMethod.Xhtml)
            result = Regex.Replace(result, @" />", "/>");

        // Unicode normalization happens BEFORE character maps so the replacement
        // strings carry through verbatim (XSLT/XQuery Serialization §6, §5.1.2).
        // QT3 Serialization-json-31 asserts NFC-composes a combining cedilla into
        // a precomposed `ç`; json-35 asserts the character-map replacement remains
        // un-normalised.
        // JSON method: per-string normalization happens inside EscapeJsonString
        // so the char-map replacement isn't re-normalised (QT3 json-35).
        if (_options.Method != OutputMethod.Json
            && !string.IsNullOrEmpty(_options.NormalizationForm)
            && !_options.NormalizationForm.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            var form = _options.NormalizationForm.ToUpperInvariant() switch
            {
                "NFC" => System.Text.NormalizationForm.FormC,
                "NFD" => System.Text.NormalizationForm.FormD,
                "NFKC" => System.Text.NormalizationForm.FormKC,
                "NFKD" => System.Text.NormalizationForm.FormKD,
                _ => (System.Text.NormalizationForm?)null
            };
            if (form is { } f)
                result = result.Normalize(f);
        }

        // Apply character maps if present.
        // JSON method: character maps are applied *inside* string content during
        // EscapeJsonString (see ApplyJsonCharacterMap) so numeric literals and
        // structural tokens are left untouched (QT3 Serialization-json-37/39).
        if (_options.CharacterMaps is { Count: > 0 } && _options.Method != OutputMethod.Json)
        {
            foreach (var (charKey, replacement) in _options.CharacterMaps)
            {
                result = result.Replace(charKey, replacement);
            }
        }

        return result;
    }

    /// <summary>StringWriter that reports the desired encoding instead of UTF-16.</summary>
    private sealed class Utf8StringWriter(System.Text.Encoding encoding) : StringWriter
    {
        public override System.Text.Encoding Encoding => encoding;
    }

    /// <summary>
    /// Static convenience method: serializes a single result item using the given store.
    /// </summary>
    /// <param name="item">The XQuery result item.</param>
    /// <param name="store">The document store for node/namespace resolution.</param>
    /// <param name="method">The output method. Defaults to <see cref="OutputMethod.Adaptive"/>.</param>
    /// <returns>The serialized string representation.</returns>
    public static string Serialize(object? item, XdmDocumentStore store, OutputMethod method = OutputMethod.Adaptive)
    {
        var serializer = new XQueryResultSerializer(store, method);
        return serializer.Serialize(item);
    }

    /// <summary>
    /// Static convenience method: serializes a single result item using the given store and options.
    /// </summary>
    /// <param name="item">The XQuery result item.</param>
    /// <param name="store">The document store for node/namespace resolution.</param>
    /// <param name="options">The serialization options.</param>
    /// <returns>The serialized string representation.</returns>
    public static string Serialize(object? item, XdmDocumentStore store, SerializationOptions options)
    {
        var serializer = new XQueryResultSerializer(store, options);
        return serializer.Serialize(item);
    }

    // Known serialization parameter names per the XSLT/XQuery Serialization spec
    private static readonly HashSet<string> KnownParamNames = new(StringComparer.Ordinal)
    {
        "method", "version", "encoding", "indent", "media-type",
        "omit-xml-declaration", "standalone", "cdata-section-elements",
        "doctype-public", "doctype-system", "byte-order-mark",
        "normalization-form", "suppress-indentation",
        "undeclare-prefixes", "use-character-maps",
        "html-version", "item-separator", "json-node-output-method",
        "allow-duplicate-names", "build-tree"
    };

    /// <summary>
    /// Parses serialization options from a parameter map (as produced by XQuery map expressions
    /// or by <see cref="ParseSerializationParamsElement"/>).
    /// Handles type coercion (xs:untypedAtomic to target types), validation, and error reporting
    /// per the XSLT/XQuery Serialization specification.
    /// </summary>
    /// <param name="paramsMap">The parameter map. May be null for defaults.</param>
    /// <param name="paramsFromMap">True if the parameters came from a user-supplied XQuery map (stricter type checking).</param>
    /// <returns>Fully validated <see cref="SerializationOptions"/>.</returns>
    public static SerializationOptions ParseSerializationOptions(
        IDictionary<object, object?>? paramsMap, bool paramsFromMap)
    {
        var method = OutputMethod.Adaptive;
        var indent = false;
        var omitXmlDeclaration = paramsFromMap; // map-based defaults to true
        string? encoding = null;
        string? standalone = null;
        string? itemSeparator = null;
        IDictionary<string, string>? characterMaps = null;
        bool allowDuplicateNames = false;
        ISet<string>? cdataSectionElements = null;
        double? htmlVersion = null;

        if (paramsMap == null)
            return SerializationOptions.Default;

        if (paramsMap.TryGetValue("method", out var m))
        {
            var ms = CoerceToString(m, "method", paramsFromMap);
            if (ms != null)
                method = ms.Trim().ToLowerInvariant() switch
                {
                    "json" => OutputMethod.Json,
                    "xml" => OutputMethod.Xml,
                    "text" => OutputMethod.Text,
                    "html" => OutputMethod.Html,
                    "xhtml" => OutputMethod.Xhtml,
                    "adaptive" => OutputMethod.Adaptive,
                    _ => OutputMethod.Adaptive
                };
        }

        if (paramsMap.TryGetValue("indent", out var ind))
            indent = CoerceToBool(ind, "indent", paramsFromMap);

        if (paramsMap.TryGetValue("omit-xml-declaration", out var omit))
            omitXmlDeclaration = CoerceToBool(omit, "omit-xml-declaration", paramsFromMap);

        if (paramsMap.TryGetValue("encoding", out var enc))
            encoding = CoerceToString(enc, "encoding", paramsFromMap);

        if (paramsMap.TryGetValue("standalone", out var sa))
        {
            if (paramsFromMap)
            {
                // From map: must be boolean or empty sequence (object?[] length 0)
                if (sa is bool bSa)
                    standalone = bSa ? "yes" : "no";
                else if (sa is XsUntypedAtomic uta)
                {
                    var sv = uta.Value.Trim().ToLowerInvariant();
                    if (sv is "true" or "1") standalone = "yes";
                    else if (sv is "false" or "0") standalone = "no";
                    else throw new XQueryRuntimeException("XPTY0004",
                        "The value of the 'standalone' serialization parameter must be a boolean");
                }
                else if (sa is object?[] saArr && saArr.Length == 0)
                    standalone = null; // empty sequence = omit
                else if (sa == null)
                    standalone = null; // empty sequence = omit
                else
                    throw new XQueryRuntimeException("XPTY0004",
                        "The value of the 'standalone' serialization parameter must be a boolean");
            }
            else if (sa is string ss)
            {
                standalone = ss.Trim().ToLowerInvariant() switch
                {
                    "yes" or "no" or "omit" => ss.Trim().ToLowerInvariant(),
                    _ => null
                };
            }
        }

        if (paramsMap.TryGetValue("item-separator", out var isep))
            itemSeparator = CoerceToString(isep, "item-separator", paramsFromMap);

        if (paramsMap.TryGetValue("allow-duplicate-names", out var adn))
            allowDuplicateNames = CoerceToBool(adn, "allow-duplicate-names", paramsFromMap);

        var jsonNodeOutputMethod = OutputMethod.Xml;
        if (paramsMap.TryGetValue("json-node-output-method", out var jnom))
        {
            var jnomStr = CoerceToString(jnom, "json-node-output-method", paramsFromMap);
            if (jnomStr != null)
                jsonNodeOutputMethod = jnomStr.Trim().ToLowerInvariant() switch
                {
                    "xml" => OutputMethod.Xml,
                    "xhtml" => OutputMethod.Xml,
                    "html" => OutputMethod.Html,
                    "text" => OutputMethod.Text,
                    _ => OutputMethod.Xml
                };
        }

        if (paramsMap.TryGetValue("html-version", out var hv))
        {
            if (hv is double dv) htmlVersion = dv;
            else if (hv is int iv) htmlVersion = iv;
            else if (hv is long lv) htmlVersion = lv;
            else if (hv is decimal decv) htmlVersion = (double)decv;
            else if (hv is XsUntypedAtomic uta && double.TryParse(uta.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pv))
                htmlVersion = pv;
        }

        if (paramsMap.TryGetValue("use-character-maps", out var ucm))
        {
            if (paramsFromMap)
            {
                // From map: must be a map(xs:string, xs:string)
                if (ucm is IDictionary<object, object?> charMapDict)
                {
                    characterMaps = new Dictionary<string, string>();
                    foreach (var (ck, cv) in charMapDict)
                    {
                        var charKey = ck.ToString() ?? "";
                        if (charKey.Length != 1)
                            throw new XQueryRuntimeException("SEPM0016",
                                $"Character map key must be a single character, got '{charKey}'");
                        // Value must be xs:string or xs:untypedAtomic (coerced to string)
                        // Elements, QNames, and other non-string types are XPTY0004
                        if (cv is XdmNode)
                            throw new XQueryRuntimeException("XPTY0004",
                                "Character map value must be xs:string, got element node");
                        if (cv is QName)
                            throw new XQueryRuntimeException("XPTY0004",
                                "Character map value must be xs:string, got xs:QName");
                        if (cv is XsUntypedAtomic uta)
                            characterMaps[charKey] = uta.Value;
                        else
                            characterMaps[charKey] = cv?.ToString() ?? "";
                    }
                }
                else if (ucm is bool)
                    throw new XQueryRuntimeException("XPTY0004",
                        "The value of the 'use-character-maps' parameter must be a map, got xs:boolean");
                else if (ucm is string)
                    throw new XQueryRuntimeException("XPTY0004",
                        "The value of the 'use-character-maps' parameter must be a map, got xs:string");
                else if (ucm != null)
                    throw new XQueryRuntimeException("XPTY0004",
                        $"The value of the 'use-character-maps' parameter must be a map");
            }
            else if (ucm is IDictionary<object, object?> charMapDict2)
            {
                characterMaps = new Dictionary<string, string>();
                foreach (var (ck, cv) in charMapDict2)
                {
                    var charKey = ck.ToString() ?? "";
                    if (charKey.Length != 1)
                        throw new XQueryRuntimeException("SEPM0016",
                            $"Character map key must be a single character, got '{charKey}'");
                    characterMaps[charKey] = cv?.ToString() ?? "";
                }
            }
        }

        if (paramsMap.TryGetValue("cdata-section-elements", out var cse))
        {
            cdataSectionElements = new HashSet<string>(StringComparer.Ordinal);
            if (cse is List<string> cseResolved)
            {
                // Pre-resolved list from ParseSerializationParamsElement (EQName/prefix names
                // already expanded to Q{uri}local form or bare local names)
                foreach (var s in cseResolved)
                    cdataSectionElements.Add(s);
            }
            else if (cse is object?[] cseArr)
            {
                foreach (var item in cseArr)
                    AddCdataQName(item, cdataSectionElements);
            }
            else if (cse is IEnumerable<object?> cseSeq && cse is not string)
            {
                foreach (var item in cseSeq)
                    AddCdataQName(item, cdataSectionElements);
            }
            else if (cse != null)
            {
                AddCdataQName(cse, cdataSectionElements);
            }
        }

        return new SerializationOptions
        {
            Method = method,
            Indent = indent,
            OmitXmlDeclaration = omitXmlDeclaration,
            Encoding = encoding,
            Standalone = standalone,
            ItemSeparator = itemSeparator,
            CharacterMaps = characterMaps,
            AllowDuplicateNames = allowDuplicateNames,
            HtmlVersion = htmlVersion,
            CdataSectionElements = cdataSectionElements,
            JsonNodeOutputMethod = jsonNodeOutputMethod
        };
    }

    /// <summary>
    /// Parses serialization parameters from an XML element
    /// (<c>&lt;output:serialization-parameters&gt;</c>) into a raw parameter map.
    /// Validates element structure per the XSLT/XQuery Serialization spec and raises
    /// SEPM0017/SEPM0018/SEPM0019 errors as appropriate.
    /// </summary>
    public static IDictionary<object, object?> ParseSerializationParamsElement(
        XdmElement root, INodeProvider? provider)
    {
        const string SerNs = "http://www.w3.org/2010/xslt-xquery-serialization";
        var dict = new Dictionary<object, object?>();
        if (provider == null) return dict;

        // Check for invalid attributes on the root element
        foreach (var rootAttrId in root.Attributes)
        {
            if (provider.GetNode(rootAttrId) is XdmAttribute rootAttr)
            {
                var attrNs = provider is XdmDocumentStore ras
                    ? ras.ResolveNamespaceUri(rootAttr.Namespace)?.ToString() ?? "" : "";
                if (rootAttr.LocalName.StartsWith("xmlns", StringComparison.Ordinal)
                    || attrNs == "http://www.w3.org/2000/xmlns/")
                    continue;
                if (string.IsNullOrEmpty(attrNs))
                    throw new XQueryRuntimeException("SEPM0017",
                        $"Attribute '{rootAttr.LocalName}' is not allowed on serialization-parameters element");
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Track non-standard namespace elements for SEPM0019 duplicate check
        var seenExtensions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var childId in root.Children)
        {
            if (provider.GetNode(childId) is not XdmElement child) continue;
            string? childNs = null;
            if (provider is XdmDocumentStore ds)
                childNs = ds.ResolveNamespaceUri(child.Namespace)?.ToString();

            if (childNs != SerNs)
            {
                if (string.IsNullOrEmpty(childNs))
                    throw new XQueryRuntimeException("SEPM0017",
                        $"Child element '{child.LocalName}' must be in the serialization namespace");
                // Non-standard namespace: check for duplicates (SEPM0019)
                var extKey = $"{{{childNs}}}{child.LocalName}";
                if (!seenExtensions.Add(extKey))
                    throw new XQueryRuntimeException("SEPM0019",
                        $"Duplicate extension serialization parameter '{child.LocalName}'");
                continue;
            }

            if (!KnownParamNames.Contains(child.LocalName))
                throw new XQueryRuntimeException("SEPM0017",
                    $"Unrecognized serialization parameter '{child.LocalName}'");
            if (!seen.Add(child.LocalName))
                throw new XQueryRuntimeException("SEPM0019",
                    $"Duplicate serialization parameter '{child.LocalName}'");

            if (child.LocalName == "use-character-maps")
            {
                // Validate use-character-maps: must not have a @value attribute
                foreach (var attrId in child.Attributes)
                {
                    if (provider.GetNode(attrId) is XdmAttribute ucmAttr)
                    {
                        var ucmAttrNs = provider is XdmDocumentStore ds3
                            ? ds3.ResolveNamespaceUri(ucmAttr.Namespace)?.ToString() ?? "" : "";
                        if (ucmAttrNs == "http://www.w3.org/2000/xmlns/"
                            || ucmAttr.LocalName.StartsWith("xmlns", StringComparison.Ordinal))
                            continue;
                        // Any non-namespace attribute on use-character-maps is SEPM0017
                        throw new XQueryRuntimeException("SEPM0017",
                            $"Attribute '{ucmAttr.LocalName}' is not allowed on use-character-maps element");
                    }
                }

                var charMaps = new Dictionary<object, object?>();
                var seenChars = new HashSet<string>(StringComparer.Ordinal);
                foreach (var mapChildId in child.Children)
                {
                    if (provider.GetNode(mapChildId) is not XdmElement mapChild) continue;

                    // Child must be output:character-map
                    string? mapChildNs = null;
                    if (provider is XdmDocumentStore ds4)
                        mapChildNs = ds4.ResolveNamespaceUri(mapChild.Namespace)?.ToString();
                    if (mapChildNs != SerNs || mapChild.LocalName != "character-map")
                        throw new XQueryRuntimeException("SEPM0017",
                            $"Invalid child element '{mapChild.LocalName}' in use-character-maps; expected 'character-map'");

                    string? character = null;
                    string? mapString = null;
                    foreach (var attrId in mapChild.Attributes)
                    {
                        if (provider.GetNode(attrId) is XdmAttribute a)
                        {
                            var aAttrNs = provider is XdmDocumentStore ds5
                                ? ds5.ResolveNamespaceUri(a.Namespace)?.ToString() ?? "" : "";
                            if (aAttrNs == "http://www.w3.org/2000/xmlns/"
                                || a.LocalName.StartsWith("xmlns", StringComparison.Ordinal))
                                continue;
                            if (a.LocalName == "character") character = a.Value;
                            else if (a.LocalName == "map-string") mapString = a.Value;
                            else
                                throw new XQueryRuntimeException("SEPM0017",
                                    $"Attribute '{a.LocalName}' is not allowed on character-map element");
                        }
                    }

                    if (character != null)
                    {
                        // SEPM0018: duplicate character mapping
                        if (!seenChars.Add(character))
                            throw new XQueryRuntimeException("SEPM0018",
                                $"Duplicate character mapping for '{character}'");
                        charMaps[character] = mapString ?? "";
                    }
                }
                dict["use-character-maps"] = charMaps;
                continue;
            }

            string? val = null;
            bool hasValue = false;
            foreach (var attrId in child.Attributes)
            {
                if (provider.GetNode(attrId) is XdmAttribute a)
                {
                    var attrNs2 = provider is XdmDocumentStore ds2
                        ? ds2.ResolveNamespaceUri(a.Namespace)?.ToString() ?? "" : "";
                    if (attrNs2 == "http://www.w3.org/2000/xmlns/"
                        || a.LocalName.StartsWith("xmlns", StringComparison.Ordinal))
                        continue;
                    if (string.IsNullOrEmpty(attrNs2))
                    {
                        if (a.LocalName == "value") { val = a.Value; hasValue = true; }
                        else throw new XQueryRuntimeException("SEPM0017",
                            $"Attribute '{a.LocalName}' is not allowed on serialization parameter element '{child.LocalName}'");
                    }
                }
            }

            if (hasValue && val != null)
            {
                var trimmedVal = val.Trim();
                switch (child.LocalName)
                {
                    case "indent":
                    case "omit-xml-declaration":
                    case "byte-order-mark":
                    case "undeclare-prefixes":
                        if (trimmedVal != "yes" && trimmedVal != "no")
                            throw new XQueryRuntimeException("SEPM0017",
                                $"Invalid value '{trimmedVal}' for serialization parameter '{child.LocalName}'");
                        break;
                    case "standalone":
                        if (trimmedVal != "yes" && trimmedVal != "no" && trimmedVal != "omit")
                            throw new XQueryRuntimeException("SEPM0017",
                                $"Invalid value '{trimmedVal}' for serialization parameter 'standalone'");
                        break;
                }
            }

            // cdata-section-elements: the @value is a whitespace-separated list of QNames
            // that may use EQName syntax (Q{uri}local), prefixed names (prefix:local), or
            // bare local names resolved against the element's default namespace.
            // Resolve each token using the in-scope namespaces of this element so that the
            // resulting set contains only expanded Q{uri}local or bare local names.
            if (child.LocalName == "cdata-section-elements" && hasValue && val != null)
            {
                var nsMap = new Dictionary<string, string>(StringComparer.Ordinal);
                string defaultNs = "";
                if (provider is XdmDocumentStore dsNs)
                {
                    foreach (var nsBinding in child.NamespaceDeclarations)
                    {
                        var uri = dsNs.ResolveNamespaceUri(nsBinding.Namespace)?.ToString() ?? "";
                        if (string.IsNullOrEmpty(nsBinding.Prefix))
                            defaultNs = uri;
                        else
                            nsMap[nsBinding.Prefix] = uri;
                    }
                }
                var resolved = new List<string>();
                foreach (var token in val.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith("Q{", StringComparison.Ordinal))
                    {
                        // EQName: Q{uri}local — already fully expanded, keep as-is
                        resolved.Add(token);
                    }
                    else if (token.Contains(':'))
                    {
                        var colon = token.IndexOf(':');
                        var prefix = token[..colon];
                        var local = token[(colon + 1)..];
                        if (nsMap.TryGetValue(prefix, out var prefixUri) && !string.IsNullOrEmpty(prefixUri))
                            resolved.Add($"Q{{{prefixUri}}}{local}");
                        else
                            resolved.Add(token); // unknown prefix: keep raw
                    }
                    else
                    {
                        // Bare local name: resolve against default namespace
                        if (!string.IsNullOrEmpty(defaultNs))
                            resolved.Add($"Q{{{defaultNs}}}{token}");
                        else
                            resolved.Add(token);
                    }
                }
                dict[child.LocalName] = resolved;
                continue;
            }

            dict[child.LocalName] = val ?? "";
        }

        return dict;
    }

    /// <summary>
    /// Coerces a serialization parameter value to boolean, handling xs:boolean, xs:untypedAtomic,
    /// and string values from XML-based parameters.
    /// </summary>
    private static bool CoerceToBool(object? value, string paramName, bool fromMap)
    {
        if (fromMap)
        {
            // From map: must be xs:boolean, or xs:untypedAtomic coerced to boolean
            if (value is bool b) return b;
            if (value is XsUntypedAtomic uta)
            {
                var sv = uta.Value.Trim().ToLowerInvariant();
                return sv switch
                {
                    "true" or "1" => true,
                    "false" or "0" => false,
                    _ => throw new XQueryRuntimeException("XPTY0004",
                        $"The value of the '{paramName}' serialization parameter must be a boolean, got xs:untypedAtomic '{uta.Value}'")
                };
            }
            if (value is string)
                throw new XQueryRuntimeException("XPTY0004",
                    $"The value of the '{paramName}' serialization parameter must be a boolean, got xs:string");
            if (value is object?[])
                throw new XQueryRuntimeException("XPTY0004",
                    $"The value of the '{paramName}' serialization parameter must be a single boolean");
            throw new XQueryRuntimeException("XPTY0004",
                $"The value of the '{paramName}' serialization parameter must be a boolean, got {value?.GetType().Name}");
        }

        // From XML: "yes"/"no" strings
        return value is true || (value is string si && si.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Coerces a serialization parameter value to string, handling xs:untypedAtomic.
    /// </summary>
    private static string? CoerceToString(object? value, string paramName, bool fromMap)
    {
        if (value == null) return null;
        if (value is string s) return s;
        if (value is XsUntypedAtomic uta) return uta.Value;
        return value.ToString();
    }

    /// <summary>
    /// Adds a QName value to the CDATA section elements set, handling QName objects
    /// and string representations.
    /// </summary>
    private static void AddCdataQName(object? item, ISet<string> set)
    {
        if (item is QName qn)
        {
            var ns = qn.ExpandedNamespace ?? qn.RuntimeNamespace ?? "";
            if (string.IsNullOrEmpty(ns))
                set.Add(qn.LocalName);
            else
                set.Add($"Q{{{ns}}}{qn.LocalName}");
        }
        else if (item is string s)
        {
            set.Add(s.Trim());
        }
        else if (item is XsUntypedAtomic uta)
        {
            set.Add(uta.Value.Trim());
        }
    }

    private void SerializeTo(object? item, TextWriter output)
    {
        // SEPM0004: standalone=yes or doctype-system requires a result that forms a
        // well-formed XML document. A single document node always qualifies; a single
        // element node also qualifies because it can be serialized with an XML declaration
        // (QT3 method-xml K2-Serialization-22/23/24). Anything else (atomic value, attribute,
        // a multi-item sequence) cannot.
        if (_method == OutputMethod.Xml
            && (_options.Standalone is "yes" or "no" || _options.DoctypeSystem != null)
            && item is not XdmDocument and not XdmElement)
        {
            throw new XQueryRuntimeException("SEPM0004",
                "Serialization error: standalone or doctype-system requires a well-formed XML document");
        }

        if (_method == OutputMethod.Json)
        {
            // XSLT/XQuery Serialization §11.1 (JSON output method): the input is
            // an XDM "JSON value" — at most ONE item — and an empty sequence
            // serializes as the literal "null". A multi-item top-level sequence
            // (or an attribute node anywhere) is a serialization error.
            // SerializeAsJson recurses into XDM arrays/maps and treats every
            // `object?[]` as the top-level sequence wrapper produced by the
            // engine when multiple items are returned.
            if (item is null)
            {
                output.Write("null");
                return;
            }
            if (item is object?[] topSeq)
            {
                if (topSeq.Length == 0)
                {
                    output.Write("null");
                    return;
                }
                if (topSeq.Length > 1)
                    throw new XQueryRuntimeException("SERE0023",
                        "JSON output method requires a single XDM item; got a sequence of " +
                        topSeq.Length + " items");
                SerializeAsJson(topSeq[0], output);
                return;
            }
            SerializeAsJson(item, output);
            return;
        }

        if (_method == OutputMethod.Html && item is XdmNode)
        {
            switch (item)
            {
                case XdmDocument doc:
                    SerializeAsHtml(doc, output);
                    return;
                case XdmElement elem:
                    SerializeAsHtml(elem, output);
                    return;
            }
        }

        if (_method == OutputMethod.Xhtml && item is XdmNode)
        {
            switch (item)
            {
                case XdmDocument xdoc:
                    SerializeAsXhtml(xdoc, output);
                    return;
                case XdmElement xelem:
                    SerializeAsXhtml(xelem, output);
                    return;
            }
        }

        // Text output method (XSLT/XQuery Serialization §8): the result is the concatenation
        // of the STRING VALUE of each item — no markup, no escaping of < & >, and comments /
        // processing instructions contribute nothing (the XDM string value of an element is
        // the concatenation of its descendant text only). A document or element therefore
        // emits its string value; a comment or PI emits the empty string; and a free-standing
        // attribute (or namespace) node cannot be serialized (SENR0001), exactly as for the
        // XML/HTML methods.
        if (_method == OutputMethod.Text && item is XdmNode textNode)
        {
            switch (textNode)
            {
                case XdmAttribute:
                    throw new XQueryRuntimeException("SENR0001",
                        "Cannot serialize a free-standing attribute node with the text output method.");
                case XdmComment:
                case XdmProcessingInstruction:
                    // A standalone comment/PI has no string-value contribution in text output.
                    return;
                case XdmText t:
                    output.Write(t.Value);
                    return;
                case XdmDocument:
                case XdmElement:
                    output.Write(textNode.StringValue ?? "");
                    return;
            }
        }

        switch (item)
        {
            case null:
                break;

            case XdmDocument doc:
                SerializeXmlNode(doc, output);
                break;

            case XdmElement elem:
                SerializeXmlNode(elem, output);
                break;

            case XdmAttribute attr:
                if (_method == OutputMethod.Adaptive)
                    // Adaptive output may serialize a free-standing attribute as name="value".
                    output.Write($"{attr.LocalName}=\"{CharacterEscaper.EscapeXmlAttribute(attr.Value)}\"");
                else if (_method is OutputMethod.Xml or OutputMethod.Html or OutputMethod.Xhtml)
                    // SENR0001: the XML/HTML output methods cannot serialize a sequence whose
                    // item is a free-standing attribute (or namespace) node — it has no element
                    // to attach to.
                    throw new XQueryRuntimeException("SENR0001",
                        "Cannot serialize a free-standing attribute node with the XML/HTML output method.");
                else
                    // text method: the attribute's string value.
                    output.Write(attr.Value);
                break;

            case XdmText text:
                output.Write(text.Value);
                break;

            case XdmComment comment:
                if (_method == OutputMethod.Xml || _method == OutputMethod.Adaptive)
                    output.Write($"<!--{comment.Value}-->");
                else
                    output.Write(comment.Value);
                break;

            case XdmProcessingInstruction pi:
                if (_method == OutputMethod.Xml || _method == OutputMethod.Adaptive)
                    output.Write($"<?{pi.Target} {pi.Value}?>");
                else
                    output.Write(pi.Value);
                break;

            case XdmNode node:
                output.Write(node.StringValue);
                break;

            // The XML and HTML output methods cannot serialize a map — there is no
            // lexical representation for it (SENR0001).
            case IDictionary<object, object?> when _method is OutputMethod.Xml or OutputMethod.Html or OutputMethod.Xhtml:
                throw new XQueryRuntimeException("SENR0001",
                    "Cannot serialize a map with the XML/HTML output method.");

            case IDictionary<object, object?> map when _method == OutputMethod.Adaptive:
                SerializeMapAdaptive(map, output);
                break;

            case IDictionary<object, object?> map:
                SerializeMapAsJson(map, output);
                break;

            // The XML/HTML output methods flatten an array, serializing each member in
            // turn (the array itself has no lexical wrapper).
            case List<object?> htmlXmlArray when _method is OutputMethod.Xml or OutputMethod.Html or OutputMethod.Xhtml:
                foreach (var member in htmlXmlArray)
                    SerializeTo(member, output);
                break;

            case List<object?> adaptiveArray when _method == OutputMethod.Adaptive:
                SerializeArrayAdaptive(adaptiveArray, output);
                break;

            // The text output method flattens an array, serializing the string value of each
            // member. Members are separated by the declared item-separator, or by a single
            // space (the default atomic-value separation for text output) — QT3
            // Serialization-text-19.
            case List<object?> textArray when _method == OutputMethod.Text:
                var firstTextMember = true;
                foreach (var member in textArray)
                {
                    if (!firstTextMember)
                        output.Write(_options.ItemSeparator ?? " ");
                    SerializeTo(member, output);
                    firstTextMember = false;
                }
                break;

            case List<object?> xdmArray:
                SerializeArrayAsJson(xdmArray, output);
                break;

            case object?[] array:
                var first = true;
                foreach (var element in array)
                {
                    if (!first)
                    {
                        if (_options.ItemSeparator != null)
                            output.Write(_options.ItemSeparator);
                        else if (_method is OutputMethod.Text or OutputMethod.Adaptive)
                            output.Write(' ');
                    }
                    SerializeTo(element, output);
                    first = false;
                }
                break;

            case IEnumerable<object?> sequence:
                var isFirst = true;
                foreach (var element in sequence)
                {
                    if (!isFirst)
                    {
                        if (_options.ItemSeparator != null)
                            output.Write(_options.ItemSeparator);
                        else if (_method is OutputMethod.Text or OutputMethod.Adaptive)
                            output.Write(' ');
                    }
                    SerializeTo(element, output);
                    isFirst = false;
                }
                break;

            case bool b:
                // All output methods (Adaptive, Xml, Json, Text, Html) write bare
                // true/false. The W3C Serialization 4.0 adaptive method uses bare
                // booleans — the function-call form (true()/false()) is the AST
                // literal representation, not the serialized form. Saxon-HE follows
                // the same convention; aligning here.
                output.Write(b ? "true" : "false");
                break;

            // W3C Serialization 4.0 §6 (Adaptive method): atomic values of a "basic"
            // type (xs:integer, xs:decimal, xs:double, xs:string, xs:boolean) serialize
            // bare, while every other (non-basic) atomic type — xs:float, the date/time
            // family, durations, the gregorian types, xs:anyURI, xs:QName, the binary
            // types — serializes in constructor notation xs:TYPE("canonicalLexical").
            // Guarded to the adaptive method so the non-adaptive numeric paths below are
            // untouched.
            // W3C Serialization 4.0 §6 (Adaptive method): the string family — xs:string
            // and all its subtypes (xs:language, xs:token, xs:NCName, …, represented as
            // XsTypedString), xs:anyURI, and xs:untypedAtomic — serialize as QUOTED
            // strings (with `"` doubled), NOT in constructor notation. This must precede
            // the typed-wrap routing so xs:anyURI no longer wraps as xs:anyURI("…").
            case Xdm.XsAnyUri anyUri when _method == OutputMethod.Adaptive:
                WriteAdaptiveQuotedString(anyUri.Value, output);
                break;
            case Xdm.XsUntypedAtomic uta when _method == OutputMethod.Adaptive:
                WriteAdaptiveQuotedString(uta.Value, output);
                break;
            case Xdm.XsTypedString typedStr when _method == OutputMethod.Adaptive:
                WriteAdaptiveQuotedString(typedStr.Value, output);
                break;

            case not null when _method == OutputMethod.Adaptive && IsAdaptiveAtomic(item):
                WriteAdaptiveAtomic(item, output);
                break;

            case double d:
                output.Write(Functions.ConcatFunction.XQueryStringValue(d));
                break;

            case float f:
                output.Write(Functions.ConcatFunction.XQueryStringValue(f));
                break;

            case string s when _method == OutputMethod.Adaptive && _options.AdaptiveQuoteStrings:
                // W3C adaptive serialization quotes top-level atomic strings, doubling
                // every `"` to `""`. Gated behind AdaptiveQuoteStrings so the facade
                // default stays bare.
                WriteAdaptiveQuotedString(s, output);
                break;

            case PhoenixmlDb.XQuery.Ast.XQueryFunction fn when _method == OutputMethod.Adaptive:
                // W3C Serialization 4.0 §6 (Adaptive method): a function item serializes
                // as prefix:local#arity for a named function reference, and as
                // (anonymous-function)#arity for an inline/anonymous function. Guarded to
                // the adaptive method only — under other methods a function item keeps the
                // existing default behavior.
                WriteFunctionItem(fn, output);
                break;

            default:
                output.Write(item.ToString());
                break;
        }
    }

    /// <summary>
    /// Serializes a map in adaptive output format: <c>map{"key":"value",...}</c>.
    /// Per W3C Serialization 4.0 §6 (Adaptive method), atomic values inside a structured
    /// item (map, array) follow these rules: xs:string → quoted, xs:boolean → bare
    /// <c>true</c>/<c>false</c>, numerics → bare lexical form. Earlier behavior emitted
    /// bare strings (<c>map{key:value}</c>), which collided with Saxon's adaptive output
    /// and tripped any caller that round-tripped the result through a JSON parser.
    /// </summary>
    private void SerializeMapAdaptive(IDictionary<object, object?> map, TextWriter output)
    {
        output.Write("map{");
        var first = true;
        foreach (var (key, value) in map)
        {
            if (!first) output.Write(',');
            first = false;
            SerializeAdaptiveStructured(key, output);
            output.Write(':');
            SerializeAdaptiveStructured(value, output);
        }
        output.Write('}');
    }

    /// <summary>
    /// Serializes an array in adaptive output format: <c>[m1,m2,…]</c>. Each member is
    /// serialized per adaptive rules via <see cref="SerializeAdaptiveMember"/>; a member
    /// that is itself a multi-item (or empty) sequence renders parenthesized
    /// (<c>(1,2,3)</c>, <c>()</c>). Previously top-level arrays in adaptive mode fell
    /// through to the JSON array path, which used the wrong format and hard-errored on
    /// sequence members and on INF/NaN numerics.
    /// </summary>
    private void SerializeArrayAdaptive(List<object?> arr, TextWriter output)
    {
        output.Write('[');
        var first = true;
        foreach (var member in arr)
        {
            if (!first) output.Write(',');
            first = false;
            SerializeAdaptiveMember(member, output);
        }
        output.Write(']');
    }

    /// <summary>
    /// Serializes a single array member in adaptive method. A member may be a multi-item
    /// sequence (represented as <c>object?[]</c>): such a member renders parenthesized,
    /// with each item serialized per adaptive structured rules. A length-1 sequence
    /// unwraps to its single item; any other value is serialized directly.
    /// </summary>
    private void SerializeAdaptiveMember(object? member, TextWriter output)
    {
        if (member is object?[] seq && seq.Length != 1)
        {
            output.Write('(');
            var first = true;
            foreach (var item in seq)
            {
                if (!first) output.Write(',');
                first = false;
                SerializeAdaptiveStructured(item, output);
            }
            output.Write(')');
        }
        else if (member is object?[] one)
        {
            SerializeAdaptiveStructured(one[0], output);
        }
        else
        {
            SerializeAdaptiveStructured(member, output);
        }
    }

    /// <summary>
    /// Serializes an item that appears INSIDE a map or array in adaptive method.
    /// Strings are quoted with JSON escaping; booleans render as the function-call
    /// form <c>true()</c>/<c>false()</c> (W3C Serialization 4.0 §6, structured-item
    /// context — distinct from the bare top-level form); nested maps/arrays
    /// recurse; everything else delegates to <see cref="SerializeTo"/>. Distinct
    /// from top-level <see cref="SerializeTo"/>, which writes strings bare because
    /// at top level the convention is "give the user the string content directly".
    /// </summary>
    private void SerializeAdaptiveStructured(object? item, TextWriter output)
    {
        switch (item)
        {
            case null:
                output.Write("()");
                break;
            case string s:
                WriteAdaptiveQuotedString(s, output);
                break;
            case bool b:
                output.Write(b ? "true()" : "false()");
                break;
            case IDictionary<object, object?> nestedMap:
                SerializeMapAdaptive(nestedMap, output);
                break;
            case List<object?> nestedArray:
                SerializeArrayAdaptive(nestedArray, output);
                break;
            case PhoenixmlDb.XQuery.Ast.XQueryFunction fn:
                // Function item nested inside a map/array: same name#arity rendering as
                // top level (we are already in adaptive method here).
                WriteFunctionItem(fn, output);
                break;
            default:
                // Numerics and other atomics fall through to the top-level adaptive
                // path (which writes bare lexical form).
                SerializeTo(item, output);
                break;
        }
    }

    /// <summary>
    /// Maps the standard function-namespace URIs to their conventional prefixes for
    /// adaptive function-item serialization (W3C Serialization 4.0 §6).
    /// </summary>
    private static readonly Dictionary<string, string> FunctionNsPrefixes = new(StringComparer.Ordinal)
    {
        ["http://www.w3.org/2005/xpath-functions"] = "fn",
        ["http://www.w3.org/2005/xpath-functions/map"] = "map",
        ["http://www.w3.org/2005/xpath-functions/array"] = "array",
        ["http://www.w3.org/2005/xpath-functions/math"] = "math",
        ["http://www.w3.org/2001/XMLSchema"] = "xs",
    };

    /// <summary>
    /// Writes a function item in adaptive method (W3C Serialization 4.0 §6). A named
    /// function reference renders as <c>prefix:local#arity</c> using the conventional
    /// prefix for a standard namespace, <c>Q{uri}local#arity</c> for an unknown
    /// namespace, or <c>local#arity</c> when the function has no namespace. An
    /// inline/anonymous function renders as <c>(anonymous-function)#arity</c>.
    /// </summary>
    private static void WriteFunctionItem(PhoenixmlDb.XQuery.Ast.XQueryFunction fn, TextWriter output)
    {
        if (fn.IsAnonymous)
        {
            output.Write("(anonymous-function)#");
            output.Write(fn.Arity.ToString(CultureInfo.InvariantCulture));
            return;
        }

        var name = fn.Name;
        // Prefer the well-known function NamespaceId mapping; fall back to any
        // EQName-expanded / runtime namespace carried on the QName.
        var uri = PhoenixmlDb.XQuery.Functions.FunctionNamespaces.ResolveNamespace(name.Namespace)
            ?? name.ResolvedNamespace;

        if (string.IsNullOrEmpty(uri))
        {
            // No namespace: bare local#arity.
            output.Write(name.LocalName);
        }
        else if (FunctionNsPrefixes.TryGetValue(uri, out var prefix))
        {
            output.Write(prefix);
            output.Write(':');
            output.Write(name.LocalName);
        }
        else
        {
            // Unknown namespace: EQName form Q{uri}local.
            output.Write("Q{");
            output.Write(uri);
            output.Write('}');
            output.Write(name.LocalName);
        }

        output.Write('#');
        output.Write(fn.Arity.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Returns true if <paramref name="item"/> is an atomic value that the adaptive
    /// method serializes via <see cref="WriteAdaptiveAtomic"/>. This covers the "basic"
    /// numeric types that need the adaptive-specific canonical double form (xs:double)
    /// plus every "non-basic" type that renders in constructor notation. xs:string and
    /// xs:boolean are intentionally excluded — they keep their dedicated bare/quoted
    /// handling in <see cref="SerializeTo"/> and <see cref="SerializeAdaptiveStructured"/>.
    /// </summary>
    private static bool IsAdaptiveAtomic(object item) => item switch
    {
        double or float => true,
        decimal or int or long or System.Numerics.BigInteger => true,
        Xdm.XsTypedInteger => true,
        Xdm.XsDateTime or Xdm.XsDate or Xdm.XsTime => true,
        Xdm.XsDuration or Xdm.YearMonthDuration => true,
        Xdm.XsGYear or Xdm.XsGYearMonth or Xdm.XsGMonth or Xdm.XsGMonthDay or Xdm.XsGDay => true,
        Xdm.XsAnyUri => true,
        QName => true,
        Xdm.XdmValue xv when xv.Type is Xdm.XdmType.HexBinary or Xdm.XdmType.Base64Binary => true,
        _ => false
    };

    /// <summary>
    /// Serializes a typed atomic value in the adaptive method (W3C Serialization 4.0 §6).
    /// "Basic" types (xs:integer, xs:decimal, xs:double) render bare; xs:double uses the
    /// canonical XPath exponential form (<c>1.0e0</c>, <c>1.5e2</c>, <c>INF</c>, <c>-INF</c>,
    /// <c>NaN</c>). Every other (non-basic) type renders in constructor notation
    /// <c>xs:TYPE("canonicalLexical")</c>, where the canonical lexical comes from the engine's
    /// existing string-value producer (<see cref="Functions.ConcatFunction.XQueryStringValue(object?)"/>)
    /// and is NOT JSON-escaped (it is a lexical form, e.g. an ISO date).
    /// </summary>
    /// <summary>
    /// Writes a string-family value (xs:string and its subtypes, xs:anyURI,
    /// xs:untypedAtomic) as an adaptive-method quoted string: a leading <c>"</c>, the
    /// content with every <c>"</c> doubled to <c>""</c> (W3C Serialization 4.0 §6 —
    /// NOT JSON backslash-escaping), and a trailing <c>"</c>.
    /// </summary>
    private static void WriteAdaptiveQuotedString(string s, TextWriter output)
    {
        output.Write('"');
        output.Write(EscapeAdaptiveString(s));
        output.Write('"');
    }

    /// <summary>
    /// Escapes a string for the adaptive output method by doubling every double-quote
    /// (<c>"</c> → <c>""</c>), per W3C Serialization 4.0 §6. This is distinct from JSON
    /// escaping (<see cref="EscapeJsonString(string)"/>), which uses backslash escapes.
    /// </summary>
    private static string EscapeAdaptiveString(string s) =>
        s.Replace("\"", "\"\"", StringComparison.Ordinal);

    private static void WriteAdaptiveAtomic(object value, TextWriter output)
    {
        switch (value)
        {
            // Basic types: bare lexical.
            case double d:
                output.Write(FormatAdaptiveDouble(d));
                return;
            case decimal or int or long or System.Numerics.BigInteger:
            case Xdm.XsTypedInteger:
                output.Write(Functions.ConcatFunction.XQueryStringValue(value));
                return;
        }

        // Non-basic types: constructor notation xs:TYPE("canonicalLexical").
        var (typeName, lexical) = AdaptiveTypedForm(value);
        output.Write("xs:");
        output.Write(typeName);
        output.Write("(\"");
        output.Write(lexical);
        output.Write("\")");
    }

    /// <summary>
    /// Maps a non-basic atomic value to its adaptive constructor type local-name and
    /// canonical lexical string. The engine collapses some XSD subtypes onto a single
    /// CLR representation, so the local-name follows the runtime type: xs:dateTimeStamp
    /// is an <see cref="Xdm.XsDateTime"/> (→ <c>dateTime</c>) and the duration subtypes
    /// collapse onto <c>duration</c> — matching the QT3 method-adaptive corpus expectations.
    /// </summary>
    private static (string TypeName, string Lexical) AdaptiveTypedForm(object value)
    {
        var lexical = Functions.ConcatFunction.XQueryStringValue(value);
        var typeName = value switch
        {
            float => "float",
            Xdm.XsDateTime => "dateTime",
            Xdm.XsDate => "date",
            Xdm.XsTime => "time",
            Xdm.XsDuration or Xdm.YearMonthDuration => "duration",
            Xdm.XsGYear => "gYear",
            Xdm.XsGYearMonth => "gYearMonth",
            Xdm.XsGMonth => "gMonth",
            Xdm.XsGMonthDay => "gMonthDay",
            Xdm.XsGDay => "gDay",
            Xdm.XsAnyUri => "anyURI",
            QName => "QName",
            Xdm.XdmValue xv when xv.Type == Xdm.XdmType.HexBinary => "hexBinary",
            Xdm.XdmValue xv when xv.Type == Xdm.XdmType.Base64Binary => "base64Binary",
            _ => value.GetType().Name
        };
        if (value is QName qn)
            lexical = qn.PrefixedName;
        return (typeName, lexical);
    }

    /// <summary>
    /// Formats an xs:double in the canonical XPath exponential form required by the adaptive
    /// method (W3C Serialization 4.0 §6): a mantissa with a decimal point and a lowercase
    /// <c>e</c> exponent, e.g. <c>1.0e0</c>, <c>1.5e2</c>, <c>-3.4e-5</c>. INF/-INF/NaN keep
    /// their special lexical forms. This is adaptive-scoped and deliberately does NOT touch
    /// the global double formatter (<see cref="Functions.ConcatFunction.XQueryStringValue(object?)"/>),
    /// which emits the fixed-point canonical form expected elsewhere (e.g. <c>1</c>).
    /// </summary>
    private static string FormatAdaptiveDouble(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "INF";
        if (double.IsNegativeInfinity(d)) return "-INF";

        // .NET "E16"/"R" round-trips, but we want the shortest canonical mantissa with a
        // single leading digit and a lowercase 'e'. Start from the round-trip "R" form and
        // normalize into m.mmme±exp.
        var sign = "";
        if (d < 0 || (d == 0.0 && double.IsNegative(d))) { sign = "-"; d = -d; }

        if (d == 0.0)
            return sign + "0.0e0";

        int exp = (int)Math.Floor(Math.Log10(d));
        double mantissa = d / Math.Pow(10, exp);
        // Guard against floating rounding pushing the mantissa to [10, ...) or below 1.
        if (mantissa >= 10.0) { mantissa /= 10.0; exp++; }
        else if (mantissa < 1.0) { mantissa *= 10.0; exp--; }

        var mantissaStr = mantissa.ToString("R", CultureInfo.InvariantCulture);
        if (!mantissaStr.Contains('.', StringComparison.Ordinal))
            mantissaStr += ".0";
        else
        {
            mantissaStr = mantissaStr.TrimEnd('0');
            if (mantissaStr[^1] == '.') mantissaStr += "0";
        }

        return $"{sign}{mantissaStr}e{exp.ToString(CultureInfo.InvariantCulture)}";
    }

    private void SerializeXmlNode(XdmNode node, TextWriter output)
    {
        // The XML declaration is emitted for a document node by default, and for a bare
        // element only when explicitly requested (omit-xml-declaration="no" or a standalone
        // value) — see ForceXmlDeclaration. omit-xml-declaration="yes" always suppresses it.
        var wantsDeclaration = !_options.OmitXmlDeclaration
            && (node is XdmDocument || (node is XdmElement && _options.ForceXmlDeclaration));

        // .NET's XmlWriter only emits a declaration in Document conformance and only writes
        // standalone via WriteStartDocument; for a forced-declaration element we manage the
        // declaration ourselves (Fragment conformance) so the element body stays a fragment.
        var manualDeclaration = wantsDeclaration && node is not XdmDocument;

        var settings = new XmlWriterSettings
        {
            Indent = _options.Indent,
            OmitXmlDeclaration = !wantsDeclaration || manualDeclaration,
            Encoding = _options.Encoding != null
                ? System.Text.Encoding.GetEncoding(_options.Encoding)
                : Encoding.UTF8,
            ConformanceLevel = node is XdmDocument ? ConformanceLevel.Document : ConformanceLevel.Fragment,
            NewLineHandling = NewLineHandling.Entitize
        };

        if (manualDeclaration)
            WriteManualXmlDeclaration(output, settings.Encoding);

        using var writer = XmlWriter.Create(output, settings);

        if (node is XdmDocument doc && wantsDeclaration)
        {
            if (_options.Standalone is "yes")
                writer.WriteStartDocument(true);
            else if (_options.Standalone is "no")
                writer.WriteStartDocument(false);
            else
                writer.WriteStartDocument();

            foreach (var childId in doc.Children)
            {
                var child = _store.GetNode(childId);
                if (child != null)
                    WriteNode(writer, child);
            }
            writer.WriteEndDocument();
        }
        else
        {
            WriteNode(writer, node);
        }
    }

    /// <summary>
    /// Writes an XML declaration directly to the output for the forced-declaration-on-element
    /// case (XmlWriter refuses a declaration in Fragment conformance). Honors the requested
    /// <c>version</c> (default 1.0), <c>encoding</c>, and <c>standalone</c> serialization
    /// parameters (QT3 method-xml K2-Serialization-18/22/23).
    /// </summary>
    private void WriteManualXmlDeclaration(TextWriter output, System.Text.Encoding encoding)
    {
        var version = string.IsNullOrEmpty(_options.Version) ? "1.0" : _options.Version;
        output.Write("<?xml version=\"");
        output.Write(version);
        output.Write("\" encoding=\"");
        output.Write(encoding.WebName.ToUpperInvariant());
        output.Write('"');
        if (_options.Standalone is "yes" or "no")
        {
            output.Write(" standalone=\"");
            output.Write(_options.Standalone);
            output.Write('"');
        }
        output.Write("?>");
    }

    /// <summary>
    /// Writes an attribute value, escaping the characters that the XML output method must
    /// emit as numeric character references (see <see cref="WriteTextEscaped"/>). In an
    /// attribute, TAB/LF/CR are additionally escaped because an XML parser normalizes
    /// attribute whitespace, so a literal would not round-trip.
    /// </summary>
    private void WriteAttributeValueEscaped(XmlWriter writer, string value)
        => WriteTextEscaped(writer, value, isAttribute: true);

    /// <summary>
    /// Writes text/attribute content, emitting numeric character references for characters
    /// that XmlWriter would otherwise pass through literally but the serialization spec
    /// requires escaped: CR (#xD), NEL (#x85), LINE SEPARATOR (#x2028), and DEL plus the
    /// C1 control range (#x7F–#x9F). In attribute content, TAB (#x9) and LF (#xA) are also
    /// escaped (attribute-value normalization). XmlWriter still handles &amp;, &lt;, &gt;,
    /// and (for attributes) the quote, for the unescaped runs written via WriteString.
    /// </summary>
    private void WriteTextEscaped(XmlWriter writer, string value, bool isAttribute)
    {
        // The text output method emits character data with no escaping at all, so the
        // numeric-character-reference rules below apply only to the markup methods.
        if (_method == OutputMethod.Text)
        {
            writer.WriteString(value);
            return;
        }

        var span = value.AsSpan();
        var start = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            var needsRef = c is '\r' or '\u0085' or '\u2028'   // CR, NEL, LINE SEPARATOR
                || (c >= '\u007F' && c <= '\u009F')             // DEL + C1 controls
                || (isAttribute && c is '\n' or '\t');          // attr whitespace normalization
            if (!needsRef)
                continue;
            if (i > start)
                writer.WriteString(span[start..i].ToString());
            writer.WriteRaw("&#x" + ((int)c).ToString("X", CultureInfo.InvariantCulture) + ";");
            start = i + 1;
        }
        if (start < span.Length)
            writer.WriteString(span[start..].ToString());
    }

    /// <summary>
    /// Determines whether <paramref name="elem"/> must have its content serialized without
    /// added indentation: either its expanded name appears in the
    /// <see cref="SerializationOptions.SuppressIndentation"/> set, or it carries an
    /// <c>xml:space="preserve"</c> attribute. The name is matched in both the qualified
    /// <c>Q{uri}local</c> form and, for a no-namespace element, the bare local name.
    /// </summary>
    private bool ShouldSuppressIndentation(XdmElement elem, string elemNs)
    {
        if (_options.SuppressIndentation is { Count: > 0 } set)
        {
            var qualified = string.IsNullOrEmpty(elemNs) ? elem.LocalName : $"Q{{{elemNs}}}{elem.LocalName}";
            if (set.Contains(qualified) || (string.IsNullOrEmpty(elemNs) && set.Contains(elem.LocalName)))
                return true;
        }

        foreach (var attrId in elem.Attributes)
        {
            if (_store.GetNode(attrId) is XdmAttribute attr
                && attr.LocalName.Equals("space", StringComparison.Ordinal)
                && string.Equals(attr.Prefix, "xml", StringComparison.Ordinal)
                && attr.Value.Trim().Equals("preserve", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Serializes the children of a suppress-indentation element to a string with indentation
    /// disabled, for injection into the parent writer via <see cref="XmlWriter.WriteRaw(string)"/>.
    /// Reuses <see cref="WriteNode"/> with <see cref="_inSuppressedSubtree"/> set so descendant
    /// elements are not re-suppressed. The element's own start/end tags are written by the
    /// caller through the parent (indenting) writer.
    /// </summary>
    private string SerializeSuppressedChildren(XdmElement elem, bool isCdataElem, string inScopeDefault)
    {
        var sw = new StringWriter();
        var settings = new XmlWriterSettings
        {
            Indent = false,
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
            NewLineHandling = NewLineHandling.Entitize
        };
        // The nested writer has no knowledge of the namespaces already in scope on the
        // suppressed element, so a child whose namespace is inherited (e.g. a default xmlns
        // declared on the suppressed element) would be re-declared. Seed the nested writer
        // with the in-scope default namespace by writing into a throw-away wrapper element
        // that declares it, then extract just the inner content (QT3 K2-Serialization-27/28).
        var saved = _inSuppressedSubtree;
        _inSuppressedSubtree = true;
        try
        {
            using var nested = XmlWriter.Create(sw, settings);
            nested.WriteStartElement("wrapper", string.IsNullOrEmpty(inScopeDefault) ? null : inScopeDefault);
            foreach (var childId in elem.Children)
            {
                var child = _store.GetNode(childId);
                if (child != null)
                    WriteNode(nested, child, isCdataElem);
            }
            nested.WriteEndElement();
        }
        finally
        {
            _inSuppressedSubtree = saved;
        }
        var raw = sw.ToString();
        // Strip the throw-away wrapper start/end tags, keeping only the inner content.
        var open = raw.IndexOf('>');
        var closeTag = raw.LastIndexOf("</wrapper>", StringComparison.Ordinal);
        if (open >= 0 && closeTag > open)
            raw = raw[(open + 1)..closeTag];
        // Match the engine-wide self-closing normalization ("<x />" -> "<x/>").
        return Regex.Replace(raw, @" />", "/>");
    }

    private void WriteNode(XmlWriter writer, XdmNode node, bool inCdataElement = false)
    {
        switch (node)
        {
            case XdmDocument doc:
                writer.WriteStartDocument();
                foreach (var childId in doc.Children)
                {
                    var child = _store.GetNode(childId);
                    if (child != null)
                        WriteNode(writer, child);
                }
                writer.WriteEndDocument();
                break;

            case XdmElement elem:
                var ns = _store.ResolveNamespaceUri(elem.Namespace)?.ToString() ?? string.Empty;

                // suppress-indentation / xml:space="preserve": when indentation is on, an
                // element listed in suppress-indentation (or carrying xml:space="preserve")
                // must have NO added whitespace inside it, while the parent still indents the
                // element itself. XmlWriter cannot toggle indentation per element, so the
                // element's children are serialized by a nested non-indenting writer and
                // injected verbatim via WriteRaw; the start tag, namespace declarations,
                // attributes, and end tag are still written through the parent writer so its
                // surrounding indentation is preserved (QT3 method-xml
                // K2-Serialization-25/26/27/36/37/40/41/42).
                var suppressHere = _options.Indent && !_inSuppressedSubtree
                    && ShouldSuppressIndentation(elem, ns);

                if (!string.IsNullOrEmpty(elem.Prefix) && !string.IsNullOrEmpty(ns))
                    writer.WriteStartElement(elem.Prefix, elem.LocalName, ns);
                else if (!string.IsNullOrEmpty(elem.Prefix))
                    // Prefix without resolved URI — write as prefixed name with dummy namespace
                    // to avoid XmlWriter "Cannot use a prefix with an empty namespace" error
                    writer.WriteStartElement(elem.Prefix, elem.LocalName, $"urn:unresolved:{elem.Prefix}");
                else
                    // Always pass the namespace URI (even empty string) so that the XmlWriter
                    // emits xmlns="" to undeclare a parent's default namespace when needed.
                    writer.WriteStartElement(elem.LocalName, ns);

                // Write namespace declarations — skip if already in scope
                // The XmlWriter tracks namespace scope automatically; we only need to
                // emit declarations for bindings NOT already established by the element's
                // own WriteStartElement or by an ancestor element.
                foreach (var nsDecl in elem.NamespaceDeclarations)
                {
                    var declUri = _store.ResolveNamespaceUri(nsDecl.Namespace)?.ToString() ?? string.Empty;

                    if (!string.IsNullOrEmpty(nsDecl.Prefix))
                    {
                        // Prefixed: skip if this prefix already resolves to the same URI
                        var inScopePrefix = writer.LookupPrefix(declUri);
                        if (inScopePrefix == nsDecl.Prefix)
                            continue;
                        writer.WriteAttributeString("xmlns", nsDecl.Prefix, null, declUri);
                    }
                    else
                    {
                        // Default namespace: skip if element already established it
                        if (ns == declUri)
                            continue;
                        writer.WriteAttributeString("xmlns", declUri);
                    }
                }

                // Write attributes
                foreach (var attrId in elem.Attributes)
                {
                    if (_store.GetNode(attrId) is XdmAttribute attr)
                    {
                        var attrNs = _store.ResolveNamespaceUri(attr.Namespace)?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(attr.Prefix))
                            writer.WriteStartAttribute(attr.Prefix, attr.LocalName, attrNs);
                        else if (!string.IsNullOrEmpty(attrNs))
                            writer.WriteStartAttribute(attr.LocalName, attrNs);
                        else
                            writer.WriteStartAttribute(attr.LocalName);
                        WriteAttributeValueEscaped(writer, attr.Value);
                        writer.WriteEndAttribute();
                    }
                }

                // Check if this element's text children should be wrapped in CDATA
                var isCdataElem = false;
                if (_options.CdataSectionElements is { Count: > 0 } cdataElems)
                {
                    // Match by local-name with namespace URI (format: "Q{uri}local" or just "local")
                    var elemNs = _store.ResolveNamespaceUri(elem.Namespace)?.ToString() ?? "";
                    var qualifiedName = string.IsNullOrEmpty(elemNs) ? elem.LocalName : $"Q{{{elemNs}}}{elem.LocalName}";
                    // Match: exact local name (for no-namespace elements),
                    // qualified Q{uri}local form, or local name when element is in no namespace
                    isCdataElem = cdataElems.Contains(qualifiedName);
                    if (!isCdataElem && string.IsNullOrEmpty(elemNs))
                        isCdataElem = cdataElems.Contains(elem.LocalName);
                }

                // Write children. For a suppress-indentation element, the children are
                // serialized without added whitespace via a nested non-indenting writer and
                // injected through WriteRaw, so the parent stops indenting inside this element
                // (the end tag therefore hugs the last child).
                if (suppressHere)
                {
                    // Children inherit this element's default namespace when it has no prefix.
                    var inScopeDefault = string.IsNullOrEmpty(elem.Prefix) ? ns : string.Empty;
                    writer.WriteRaw(SerializeSuppressedChildren(elem, isCdataElem, inScopeDefault));
                }
                else
                {
                    foreach (var childId in elem.Children)
                    {
                        var child = _store.GetNode(childId);
                        if (child != null)
                            WriteNode(writer, child, isCdataElem);
                    }
                }

                writer.WriteEndElement();
                break;

            case XdmText text:
                if (_options.CdataSectionElements is { Count: > 0 } && inCdataElement)
                    writer.WriteCData(text.Value);
                else
                    WriteTextEscaped(writer, text.Value, isAttribute: false);
                break;

            case XdmComment comment:
                writer.WriteComment(comment.Value);
                break;

            case XdmProcessingInstruction pi:
                writer.WriteProcessingInstruction(pi.Target, pi.Value);
                break;
        }
    }

    private void SerializeArrayAsJson(List<object?> array, TextWriter output, int depth = 0)
    {
        output.Write('[');
        for (int i = 0; i < array.Count; i++)
        {
            if (i > 0) output.Write(',');
            SerializeAsJson(array[i], output, depth + 1);
        }
        output.Write(']');
    }

    private void SerializeMapAsJson(IDictionary<object, object?> map, TextWriter output, int depth = 0)
    {
        var indent = _options.Indent;
        var newline = indent ? "\n" : "";
        var innerIndent = indent ? new string(' ', (depth + 1) * 2) : "";
        var outerIndent = indent ? new string(' ', depth * 2) : "";

        // SERE0022: check for duplicate string keys (QName and string "foo" both map to "foo")
        // When allow-duplicate-names is true, duplicates are permitted (last value wins for parsers)
        if (!_options.AllowDuplicateNames)
        {
            var seenKeys = new HashSet<string>();
            foreach (var key in map.Keys)
            {
                var keyStr = key.ToString() ?? "";
                if (!seenKeys.Add(keyStr))
                    throw new PhoenixmlDb.XQuery.Execution.XQueryRuntimeException("SERE0022",
                        $"Duplicate key '{keyStr}' in JSON map serialization");
            }
        }

        // SERE0023: map values that are sequences (object[]) with length != 1 are not valid JSON
        foreach (var (key, value) in map)
        {
            if (value is object?[] valArr && valArr.Length != 1)
                throw new PhoenixmlDb.XQuery.Execution.XQueryRuntimeException("SERE0023",
                    $"JSON output method cannot serialize a sequence as map value for key '{key}'");
        }

        output.Write('{');
        var first = true;
        foreach (var (key, value) in map)
        {
            if (!first) output.Write(',');
            output.Write(newline);
            if (indent) output.Write(innerIndent);
            output.Write('"');
            output.Write(EscapeJsonString(key.ToString() ?? ""));
            output.Write('"');
            output.Write(':');
            if (indent) output.Write(' ');
            SerializeAsJson(value, output, depth + 1);
            first = false;
        }
        if (!first)
        {
            output.Write(newline);
            if (indent) output.Write(outerIndent);
        }
        output.Write('}');
    }

    private void SerializeAsJson(object? item, TextWriter output, int depth = 0)
    {
        var indent = _options.Indent;
        var newline = indent ? "\n" : "";
        var innerIndent = indent ? new string(' ', (depth + 1) * 2) : "";
        var outerIndent = indent ? new string(' ', depth * 2) : "";

        switch (item)
        {
            case null:
                output.Write("null");
                break;
            case bool b:
                output.Write(b ? "true" : "false");
                break;
            case double d when double.IsNaN(d) || double.IsInfinity(d):
                throw new PhoenixmlDb.XQuery.Execution.XQueryRuntimeException("SERE0020",
                    "JSON output method cannot serialize NaN or Infinity");
            case float f when float.IsNaN(f) || float.IsInfinity(f):
                throw new PhoenixmlDb.XQuery.Execution.XQueryRuntimeException("SERE0020",
                    "JSON output method cannot serialize NaN or Infinity");
            case int or long or double or float or decimal or BigInteger or Xdm.XsTypedInteger:
                // XsTypedInteger carries a derived-integer subtype tag (xs:int,
                // xs:unsignedShort, …); for JSON it renders as a bare integer via its
                // IConvertible string form. (QT3 Serialization-json-10/18.)
                output.Write(FormatJsonNumber(item));
                break;
            case string s:
                output.Write('"');
                output.Write(EscapeJsonString(s));
                output.Write('"');
                break;
            case IDictionary<object, object?> nestedMap:
                SerializeMapAsJson(nestedMap, output, depth);
                break;
            case XdmAttribute:
                // SENR0001: serializing a free-standing attribute node is undefined
                // for JSON output (XSLT/XQuery Serialization §11.1).
                throw new XQueryRuntimeException("SENR0001",
                    "JSON output method cannot serialize an attribute node");
            case object?[] array:
                // Sequence wrapper inside an XDM array (or anywhere except the
                // top level — top-level sequences are handled in SerializeTo).
                // JSON requires single-item entries, so a multi-item sequence is
                // SERE0023 (§11.1: each entry of an array must be a single JSON
                // value).
                if (array.Length > 1)
                    throw new XQueryRuntimeException("SERE0023",
                        "JSON array entry / map value cannot be a sequence of multiple items");
                if (array.Length == 0)
                {
                    output.Write("null");
                }
                else
                {
                    SerializeAsJson(array[0], output, depth);
                }
                break;
            case IList<object?> list:
                output.Write('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) output.Write(',');
                    output.Write(newline);
                    if (indent) output.Write(innerIndent);
                    SerializeAsJson(list[i], output, depth + 1);
                }
                if (list.Count > 0)
                {
                    output.Write(newline);
                    if (indent) output.Write(outerIndent);
                }
                output.Write(']');
                break;
            case XdmNode node:
                // JSON serialization of nodes: serialize as XML markup string
                var nodeXml = SerializeNodeForJson(node);
                output.Write('"');
                output.Write(EscapeJsonString(nodeXml));
                output.Write('"');
                break;
            default:
                output.Write('"');
                output.Write(EscapeJsonString(item.ToString() ?? ""));
                output.Write('"');
                break;
        }
    }

    /// <summary>
    /// Serializes a node to its XML string representation for use in JSON output.
    /// </summary>
    private string SerializeNodeForJson(XdmNode node)
    {
        // Text mode (json-node-output-method=text): collapse to the node's string-value,
        // no markup, no escaping beyond what JSON's own EscapeJsonString adds afterwards
        // (QT3 Serialization-json-52: <e>hi</e> → "hi").
        if (_options.JsonNodeOutputMethod == OutputMethod.Text)
            return node.StringValue ?? "";

        // Per XSLT/XQuery Serialization §11.1, a node embedded in a JSON value is
        // serialized using the json-node-output-method (default xml) and the result is
        // then enclosed in quotes. For a text node that means XML-escaping `<`, `&`,
        // `>` — so `text { "<" }` becomes `"&lt;"`, not `"<"` (QT3 Serialization-json-44).
        if (node is XdmText text)
        {
            var escaped = System.Security.SecurityElement.Escape(text.Value) ?? "";
            // XML 1.0 §2.11 / Serialization §5.1.4: a literal #xD in text content
            // must be serialized as the character reference `&#13;` (or `&#xD;`)
            // because the parser would otherwise normalise it to #xA. JSON wraps
            // the resulting string in quotes and JSON-escapes it (QT3 json-55).
            return escaped.Replace("\r", "&#13;");
        }

        // For document nodes, serialize children
        if (node is XdmDocument doc)
        {
            var sb = new StringBuilder();
            foreach (var childId in doc.Children)
            {
                var child = _store.GetNode(childId);
                if (child is XdmNode childNode)
                    sb.Append(SerializeNodeForJson(childNode));
            }
            return sb.ToString();
        }

        // For elements and other nodes, serialize as XML/HTML fragment per the
        // json-node-output-method option (default xml).
        var xmlOptions = new SerializationOptions
        {
            Method = _options.JsonNodeOutputMethod == OutputMethod.Html ? OutputMethod.Html : OutputMethod.Xml,
            OmitXmlDeclaration = true
        };
        var xmlSerializer = new XQueryResultSerializer(_store, xmlOptions);
        return xmlSerializer.Serialize(node);
    }

    /// <summary>
    /// Formats a numeric XDM value for JSON output per XSLT/XQuery Serialization §11.1:
    /// integers print without a decimal point, decimals print in fixed notation with
    /// trailing zeros stripped, and xs:double / xs:float follow the XPath cast-to-string
    /// algorithm (fixed for |v| in [1e-6, 1e21), scientific otherwise). Without this,
    /// Convert.ToString routed through .NET defaults, producing "1E-05" where the JSON
    /// spec (QT3 Serialization-json-16) requires "0.00001".
    /// </summary>
    private static string FormatJsonNumber(object value) => value switch
    {
        double d => PhoenixmlDb.XQuery.Functions.XmlToJsonFunction.FormatJsonNumber(d),
        float f => PhoenixmlDb.XQuery.Functions.XmlToJsonFunction.FormatJsonNumber((double)f),
        // Integer and decimal types print with culture-invariant ToString — decimals
        // shouldn't carry trailing zeros, but xs:decimal preserves them; matches Saxon.
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
    };

    private string EscapeJsonString(string s)
    {
        // Per XSLT/XQuery Serialization §6 and §11.4, for JSON the order is:
        //  (1) Unicode-normalise each string value (NFC etc., if requested).
        //  (2) For each input character: if it is in the character map, emit
        //      the replacement verbatim (no further JSON escaping — QT3 json-36).
        //      Otherwise apply JSON escaping. This guarantees char-map output
        //      is preserved even when it would otherwise need escaping, and
        //      that NFC does not re-compose the char-map replacement (json-35).
        if (!string.IsNullOrEmpty(_options.NormalizationForm)
            && !_options.NormalizationForm.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            var form = _options.NormalizationForm.ToUpperInvariant() switch
            {
                "NFC" => System.Text.NormalizationForm.FormC,
                "NFD" => System.Text.NormalizationForm.FormD,
                "NFKC" => System.Text.NormalizationForm.FormKC,
                "NFKD" => System.Text.NormalizationForm.FormKD,
                _ => (System.Text.NormalizationForm?)null
            };
            if (form is { } f)
                s = s.Normalize(f);
        }

        var charMap = _options.CharacterMaps;
        var sb = new StringBuilder(s.Length);
        var targetEncoding = _options.Encoding != null
            ? System.Text.Encoding.GetEncoding(_options.Encoding)
            : null;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (charMap is { Count: > 0 } && charMap.TryGetValue(c.ToString(), out var replacement))
            {
                sb.Append(replacement);
                continue;
            }
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '/': sb.Append("\\/"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append($"\\u{(int)c:X4}");
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        // Non-BMP character: check if target encoding can represent it
                        if (targetEncoding != null && !CanEncode(targetEncoding, s.Substring(i, 2)))
                        {
                            // Emit as surrogate pair escapes
                            sb.Append($"\\u{(int)c:X4}");
                            i++;
                            sb.Append($"\\u{(int)s[i]:X4}");
                        }
                        else
                        {
                            sb.Append(c);
                            i++;
                            sb.Append(s[i]);
                        }
                    }
                    else if (targetEncoding != null && c > '\u007F' && !CanEncode(targetEncoding, c.ToString()))
                    {
                        sb.Append($"\\u{(int)c:X4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Checks whether the target encoding can represent the given string without data loss.
    /// </summary>
    private static bool CanEncode(System.Text.Encoding encoding, string s)
    {
        // Create a lossy encoder that replaces unknown chars with '?'
        var encoder = encoding.GetEncoder();
        encoder.Fallback = EncoderFallback.ReplacementFallback;
        var bytes = encoding.GetBytes(s);
        var roundTrip = encoding.GetString(bytes);
        return roundTrip == s;
    }

    // HTML void elements that must not have a closing tag and are emitted minimized
    // (e.g. <br> not <br/>). Includes the HTML5 set plus legacy HTML 4.0 empty
    // elements (frame, isindex, keygen, basefont, command, image) so both the 4.0
    // and 5.0 serialization variants minimize correctly.
    private static readonly HashSet<string> HtmlVoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
        "frame", "isindex", "keygen", "basefont", "command", "image"
    };

    // HTML raw-text (and escapable-raw-text) elements whose text content is written
    // without escaping '<' or '&' per the HTML output method.
    private static readonly HashSet<string> HtmlRawTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style"
    };

    // The XHTML namespace; elements in this namespace are treated as HTML elements
    // by the HTML output method.
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    /// <summary>
    /// Serializes a node as HTML output, emitting DOCTYPE, meta charset, void elements, etc.
    /// </summary>
    private void SerializeAsHtml(XdmNode node, TextWriter output)
    {
        WriteHtmlDoctype(output);
        if (node is XdmDocument doc)
        {
            foreach (var childId in doc.Children)
            {
                var child = _store.GetNode(childId);
                if (child is XdmNode childNode)
                    WriteHtmlNode(childNode, output, 0);
            }
        }
        else if (node is XdmElement elem)
        {
            WriteHtmlNode(elem, output, 0);
        }
    }

    /// <summary>
    /// Emits the leading DOCTYPE for the HTML method, if any. A DOCTYPE with a
    /// public and/or system identifier is always emitted; otherwise an HTML5
    /// <c>&lt;!DOCTYPE html&gt;</c> is emitted only when the requested version is 5.x
    /// (HTML 4.0 output carries no implicit DOCTYPE).
    /// </summary>
    private void WriteHtmlDoctype(TextWriter output)
    {
        var pub = _options.DoctypePublic;
        var sys = _options.DoctypeSystem;
        if (!string.IsNullOrEmpty(pub) || !string.IsNullOrEmpty(sys))
        {
            output.Write("<!DOCTYPE html");
            if (!string.IsNullOrEmpty(pub))
            {
                output.Write(" PUBLIC \"");
                output.Write(pub);
                output.Write('"');
                if (!string.IsNullOrEmpty(sys))
                {
                    output.Write(" \"");
                    output.Write(sys);
                    output.Write('"');
                }
            }
            else
            {
                output.Write(" SYSTEM \"");
                output.Write(sys);
                output.Write('"');
            }
            output.Write('>');
            if (_options.Indent) output.WriteLine();
            return;
        }

        if (IsHtml5())
        {
            output.Write("<!DOCTYPE html>");
            if (_options.Indent) output.WriteLine();
        }
    }

    /// <summary>True when the requested HTML version is 5.x (default for the HTML method is 5.0).</summary>
    private bool IsHtml5()
    {
        if (_options.HtmlVersion is double hv)
            return hv >= 5.0;
        var v = _options.Version;
        if (string.IsNullOrEmpty(v))
            return true; // default HTML version is 5.0
        return v.StartsWith('5');
    }

    private string? ResolveNs(NamespaceId ns) => _store.ResolveNamespaceUri(ns)?.ToString();

    private void WriteHtmlNode(XdmNode node, TextWriter output, int depth, bool suppressIndent = false, string parentNs = "")
    {
        switch (node)
        {
            case XdmElement elem:
                var elemNs = ResolveNs(elem.Namespace) ?? "";
                // Elements outside the HTML/XHTML namespace are serialized using XML
                // rules (with namespace declarations and self-closing empty tags).
                if (elemNs.Length != 0 &&
                    !string.Equals(elemNs, XhtmlNamespace, StringComparison.Ordinal))
                {
                    SerializeXmlNode(elem, output);
                    return;
                }

                var localName = elem.LocalName;
                output.Write('<');
                output.Write(localName);
                // Declare the XHTML namespace when entering it from a different namespace,
                // so XHTML-namespace input round-trips its xmlns under the HTML method.
                if (string.Equals(elemNs, XhtmlNamespace, StringComparison.Ordinal) &&
                    !string.Equals(parentNs, XhtmlNamespace, StringComparison.Ordinal))
                {
                    output.Write(" xmlns=\"");
                    output.Write(XhtmlNamespace);
                    output.Write('"');
                }
                WriteHtmlAttributes(elem, output);
                output.Write('>');

                // Inject the content-type <meta> into <head> when include-content-type
                // is on. Any pre-existing content-type meta is dropped (skipped while
                // writing children) so exactly one canonical declaration is emitted.
                var injectMeta = localName.Equals("head", StringComparison.OrdinalIgnoreCase)
                    && _options.IncludeContentType;
                if (injectMeta)
                {
                    var enc = _options.Encoding ?? "UTF-8";
                    var media = _options.MediaType ?? "text/html";
                    output.Write("<meta http-equiv=\"Content-Type\" content=\"");
                    output.Write(media);
                    output.Write("; charset=");
                    output.Write(enc);
                    output.Write("\">");
                }

                var isRawText = HtmlRawTextElements.Contains(localName);
                if (isRawText)
                {
                    // Raw-text elements (script/style): all descendant content is written
                    // verbatim with no escaping of '<' or '&'.
                    foreach (var childId in elem.Children)
                    {
                        if (_store.GetNode(childId) is XdmNode rawChild)
                            WriteHtmlRawText(rawChild, output);
                    }
                }
                else
                {
                    WriteHtmlChildren(elem, output, depth, suppressIndent, skipContentTypeMeta: injectMeta, parentNs: elemNs);
                }

                // Void elements: no closing tag.
                if (!HtmlVoidElements.Contains(localName))
                {
                    output.Write("</");
                    output.Write(localName);
                    output.Write('>');
                }
                break;

            case XdmText text:
                output.Write(CharacterEscaper.EscapeXmlText(text.Value));
                break;

            case XdmComment comment:
                output.Write("<!--");
                output.Write(comment.Value);
                output.Write("-->");
                break;

            case XdmProcessingInstruction pi:
                // The HTML processing-instruction form has no trailing '?': <?target value>.
                output.Write("<?");
                output.Write(pi.Target);
                if (!string.IsNullOrEmpty(pi.Value))
                {
                    output.Write(' ');
                    output.Write(pi.Value);
                }
                output.Write('>');
                break;
        }
    }

    private void WriteHtmlChildren(XdmElement elem, TextWriter output, int depth, bool suppressIndent, bool skipContentTypeMeta, string parentNs)
    {
        // Indentation is added only for "block" content: the element has element
        // children and no significant (non-whitespace) text child. Mixed content
        // (e.g. <p>a<a>x</a>z</p>) is emitted without inserted whitespace so the HTML
        // method never alters rendered text. suppress-indentation disables it for the
        // element and all of its descendants.
        var childSuppressed = suppressIndent || IsSuppressedForIndent(elem);
        var indent = _options.Indent && !childSuppressed && IsBlockContent(elem);
        var childDepth = depth + 1;
        foreach (var childId in elem.Children)
        {
            var child = _store.GetNode(childId);
            if (child is not XdmNode childNode)
                continue;
            if (skipContentTypeMeta && childNode is XdmElement maybeMeta && IsContentTypeMeta(maybeMeta))
                continue;
            if (indent && childNode is XdmElement)
            {
                output.WriteLine();
                for (var i = 0; i < childDepth; i++) output.Write("   ");
            }
            WriteHtmlNode(childNode, output, childDepth, childSuppressed, parentNs);
        }
        if (indent)
        {
            output.WriteLine();
            for (var i = 0; i < depth; i++) output.Write("   ");
        }
    }

    private void WriteHtmlRawText(XdmNode node, TextWriter output)
    {
        switch (node)
        {
            case XdmText t:
                output.Write(t.Value);
                break;
            case XdmElement e:
                output.Write('<');
                output.Write(e.LocalName);
                // Inside raw-text content nothing is escaped, including attribute values.
                foreach (var attrId in e.Attributes)
                {
                    if (_store.GetNode(attrId) is not XdmAttribute a) continue;
                    output.Write(' ');
                    output.Write(a.LocalName);
                    output.Write("=\"");
                    output.Write(a.Value);
                    output.Write('"');
                }
                output.Write('>');
                foreach (var childId in e.Children)
                    if (_store.GetNode(childId) is XdmNode c)
                        WriteHtmlRawText(c, output);
                output.Write("</");
                output.Write(e.LocalName);
                output.Write('>');
                break;
        }
    }

    private bool IsSuppressedForIndent(XdmElement elem)
    {
        var set = _options.SuppressIndentation;
        if (set == null || set.Count == 0) return false;
        var ns = ResolveNs(elem.Namespace) ?? "";
        // Matched either as Q{uri}local or as a bare local name (the harness emits both
        // forms). HTML element names are case-insensitive, so match accordingly.
        var qname = "Q{" + ns + "}" + elem.LocalName;
        foreach (var entry in set)
        {
            if (string.Equals(entry, qname, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry, elem.LocalName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool IsBlockContent(XdmElement elem)
    {
        var hasElementChild = false;
        foreach (var childId in elem.Children)
        {
            var child = _store.GetNode(childId);
            if (child is XdmElement)
                hasElementChild = true;
            else if (child is XdmText t && !string.IsNullOrWhiteSpace(t.Value))
                return false; // mixed content: never indent
        }
        return hasElementChild;
    }

    private bool IsContentTypeMeta(XdmElement elem)
    {
        if (!elem.LocalName.Equals("meta", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var attrId in elem.Attributes)
        {
            if (_store.GetNode(attrId) is XdmAttribute a &&
                a.LocalName.Equals("http-equiv", StringComparison.OrdinalIgnoreCase) &&
                a.Value.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void WriteHtmlAttributes(XdmElement elem, TextWriter output)
    {
        foreach (var attrId in elem.Attributes)
        {
            if (_store.GetNode(attrId) is not XdmAttribute attr)
                continue;
            output.Write(' ');
            output.Write(attr.LocalName);
            // Boolean-attribute minimization: when the value equals the attribute name
            // (case-insensitively), the HTML method emits the name alone (e.g. selected).
            if (string.Equals(attr.Value, attr.LocalName, StringComparison.OrdinalIgnoreCase))
                continue;
            output.Write("=\"");
            var value = IsUriAttribute(elem.LocalName, attr.LocalName) && _options.EscapeUriAttributes
                ? EscapeUriAttributeValue(attr.Value)
                : attr.Value;
            output.Write(EscapeHtmlAttribute(value));
            output.Write('"');
        }
    }

    // HTML attribute-value escaping: like XML, but a bare '&' that does not introduce a
    // character/entity reference (i.e. not followed by a letter, digit, or '#') is left
    // literal per the HTML output method (e.g. "&{entspannend}" stays as-is).
    private static string EscapeHtmlAttribute(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            switch (ch)
            {
                case '&':
                    var next = i + 1 < value.Length ? value[i + 1] : '\0';
                    if (char.IsLetterOrDigit(next) || next == '#')
                        sb.Append("&amp;");
                    else
                        sb.Append('&');
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    // Attributes whose value is a URI for the purposes of escape-uri-attributes.
    private static bool IsUriAttribute(string elementName, string attrName)
    {
        _ = elementName;
        return attrName.Equals("href", StringComparison.OrdinalIgnoreCase)
            || attrName.Equals("src", StringComparison.OrdinalIgnoreCase)
            || attrName.Equals("cite", StringComparison.OrdinalIgnoreCase)
            || attrName.Equals("action", StringComparison.OrdinalIgnoreCase)
            || attrName.Equals("longdesc", StringComparison.OrdinalIgnoreCase)
            || attrName.Equals("usemap", StringComparison.OrdinalIgnoreCase)
            || attrName.Equals("background", StringComparison.OrdinalIgnoreCase);
    }

    // %-escapes non-ASCII characters in a URI-valued attribute, encoding each as its
    // UTF-8 byte sequence per the HTML output method's escape-uri-attributes parameter.
    // ASCII characters (including spaces) are left intact, matching the W3C expectation.
    private static string EscapeUriAttributeValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch >= 0x7F)
            {
                foreach (var b in Encoding.UTF8.GetBytes(ch.ToString()))
                    sb.Append('%').Append(((int)b).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    // ===== XHTML output method =====
    // The XHTML output method produces well-formed XML (real escaping, namespaces,
    // self-closing empty elements) plus HTML-compatibility rules: a space before the
    // self-closing slash for VOID elements (<br />), separate start/end tags for empty
    // NON-void elements (<p></p>), an HTML-style DOCTYPE, and an optional content-type
    // <meta>. Void-element recognition is version-dependent: HTML 5.0 recognizes them
    // in any namespace; HTML 4.0 only inside the XHTML namespace.

    // HTML 4.0 elements with an empty content model (recognized only inside the XHTML
    // namespace by the XHTML method when html-version is 4.x).
    private static readonly HashSet<string> Xhtml4VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "isindex", "link", "meta", "param", "frame", "basefont"
    };

    // HTML 5.0 void elements (recognized in any namespace by the XHTML method when
    // html-version is 5.x). Note: frame/isindex are NOT void in HTML 5.0.
    private static readonly HashSet<string> Xhtml5VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "keygen", "link", "meta", "param", "source", "track", "wbr"
    };

    /// <summary>Serializes a node using the XHTML output method.</summary>
    private void SerializeAsXhtml(XdmNode node, TextWriter output)
    {
        WriteXhtmlDoctype(output);
        if (node is XdmDocument doc)
        {
            foreach (var childId in doc.Children)
            {
                if (_store.GetNode(childId) is XdmNode childNode)
                    WriteXhtmlNode(childNode, output, 0, parentNs: "", suppressIndent: false);
            }
        }
        else
        {
            WriteXhtmlNode(node, output, 0, parentNs: "", suppressIndent: false);
        }
    }

    /// <summary>
    /// Emits the leading DOCTYPE for the XHTML method. HTML 5.0 always emits
    /// <c>&lt;!DOCTYPE html&gt;</c> (a bare doctype-public is ignored). HTML 4.0 emits a
    /// DOCTYPE only when a doctype-system is present (a bare doctype-public is dropped).
    /// When both public and system identifiers are present a PUBLIC declaration is emitted.
    /// </summary>
    private void WriteXhtmlDoctype(TextWriter output)
    {
        var pub = _options.DoctypePublic;
        var sys = _options.DoctypeSystem;
        var html5 = IsHtml5();

        if (!string.IsNullOrEmpty(sys))
        {
            output.Write("<!DOCTYPE html");
            if (!string.IsNullOrEmpty(pub))
            {
                output.Write(" PUBLIC \"");
                output.Write(pub);
                output.Write("\" \"");
                output.Write(sys);
                output.Write('"');
            }
            else
            {
                output.Write(" SYSTEM \"");
                output.Write(sys);
                output.Write('"');
            }
            output.Write('>');
            if (_options.Indent) output.WriteLine();
            return;
        }

        // No system identifier: HTML 5.0 emits a bare <!DOCTYPE html>; HTML 4.0 emits
        // nothing (a doctype-public with no system is dropped).
        if (html5)
        {
            output.Write("<!DOCTYPE html>");
            if (_options.Indent) output.WriteLine();
        }
    }

    /// <summary>True when the element is a void (empty-content-model) element under the
    /// XHTML method, given the requested html-version and the element's namespace.</summary>
    private bool IsXhtmlVoid(string localName, string elemNs)
    {
        if (IsHtml5())
        {
            // HTML 5.0: void elements recognized in any namespace.
            return Xhtml5VoidElements.Contains(localName);
        }
        // HTML 4.0: void elements recognized only inside the XHTML namespace.
        return string.Equals(elemNs, XhtmlNamespace, StringComparison.Ordinal)
            && Xhtml4VoidElements.Contains(localName);
    }

    private void WriteXhtmlNode(XdmNode node, TextWriter output, int depth, string parentNs, bool suppressIndent)
    {
        switch (node)
        {
            case XdmElement elem:
                WriteXhtmlElement(elem, output, depth, parentNs, suppressIndent);
                break;

            case XdmText text:
                output.Write(CharacterEscaper.EscapeXmlText(text.Value));
                break;

            case XdmComment comment:
                output.Write("<!--");
                output.Write(comment.Value);
                output.Write("-->");
                break;

            case XdmProcessingInstruction pi:
                output.Write("<?");
                output.Write(pi.Target);
                if (!string.IsNullOrEmpty(pi.Value))
                {
                    output.Write(' ');
                    output.Write(pi.Value);
                }
                output.Write("?>");
                break;
        }
    }

    private void WriteXhtmlElement(XdmElement elem, TextWriter output, int depth, string parentNs, bool suppressIndent)
    {
        var elemNs = ResolveNs(elem.Namespace) ?? "";
        var localName = elem.LocalName;

        output.Write('<');
        output.Write(localName);

        // Default-namespace declaration: emit when the element's namespace differs from
        // the parent's (prefix normalization — XHTML/SVG/MathML inputs round-trip as a
        // default namespace, never with a prefix).
        if (!string.Equals(elemNs, parentNs, StringComparison.Ordinal))
        {
            output.Write(" xmlns=\"");
            output.Write(CharacterEscaper.EscapeXmlAttribute(elemNs));
            output.Write('"');
        }

        WriteXhtmlAttributes(elem, output);

        // Inject the content-type <meta> into <head> (XHTML namespace or no namespace)
        // when include-content-type is on, dropping any pre-existing content-type meta.
        var injectMeta = localName.Equals("head", StringComparison.OrdinalIgnoreCase)
            && _options.IncludeContentType
            && (elemNs.Length == 0 || string.Equals(elemNs, XhtmlNamespace, StringComparison.Ordinal));

        var isVoid = IsXhtmlVoid(localName, elemNs);
        var isRawText = (elemNs.Length == 0 || string.Equals(elemNs, XhtmlNamespace, StringComparison.Ordinal))
            && HtmlRawTextElements.Contains(localName);

        // Determine whether there is any child content to emit (after meta injection).
        var hasChildren = HasXhtmlChildren(elem) || injectMeta;

        if (isVoid && !hasChildren)
        {
            // Void element with no content: self-close with a space before the slash.
            output.Write(" />");
            return;
        }

        output.Write('>');

        if (injectMeta)
        {
            var enc = _options.Encoding ?? "UTF-8";
            var media = _options.MediaType ?? "text/html";
            output.Write("<meta http-equiv=\"Content-Type\" content=\"");
            output.Write(media);
            output.Write("; charset=");
            output.Write(enc);
            output.Write("\" />");
        }

        var isCdataElem = IsXhtmlCdataElement(elem, elemNs);

        if (isRawText && !isCdataElem)
        {
            // script/style raw-text: descendant text written verbatim (no escaping).
            foreach (var childId in elem.Children)
                if (_store.GetNode(childId) is XdmNode rawChild)
                    WriteHtmlRawText(rawChild, output);
        }
        else
        {
            WriteXhtmlChildren(elem, output, depth, elemNs, skipContentTypeMeta: injectMeta,
                cdata: isCdataElem, suppressIndent: suppressIndent);
        }

        output.Write("</");
        output.Write(localName);
        output.Write('>');
    }

    private bool HasXhtmlChildren(XdmElement elem)
    {
        foreach (var childId in elem.Children)
            if (_store.GetNode(childId) is XdmNode)
                return true;
        return false;
    }

    private void WriteXhtmlChildren(XdmElement elem, TextWriter output, int depth, string elemNs,
        bool skipContentTypeMeta, bool cdata, bool suppressIndent)
    {
        // suppress-indentation applies to the element AND all of its descendants.
        var childSuppressed = suppressIndent || IsSuppressedForIndent(elem);
        var indent = _options.Indent && !childSuppressed && IsBlockContent(elem);
        var childDepth = depth + 1;
        foreach (var childId in elem.Children)
        {
            if (_store.GetNode(childId) is not XdmNode childNode)
                continue;
            if (skipContentTypeMeta && childNode is XdmElement maybeMeta && IsContentTypeMeta(maybeMeta))
                continue;
            if (cdata && childNode is XdmText cdataText)
            {
                WriteXhtmlCdataText(cdataText.Value, output);
                continue;
            }
            if (indent && childNode is XdmElement)
            {
                output.WriteLine();
                for (var i = 0; i < childDepth; i++) output.Write("   ");
            }
            WriteXhtmlNode(childNode, output, childDepth, elemNs, childSuppressed);
        }
        if (indent)
        {
            output.WriteLine();
            for (var i = 0; i < depth; i++) output.Write("   ");
        }
    }

    // Writes a text node wrapped in a CDATA section, splitting on any "]]>" so the
    // marked section stays well-formed (the "]]>" is emitted as "]]]]><![CDATA[>").
    private static void WriteXhtmlCdataText(string value, TextWriter output)
    {
        output.Write("<![CDATA[");
        output.Write(value.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal));
        output.Write("]]>");
    }

    private bool IsXhtmlCdataElement(XdmElement elem, string elemNs)
    {
        if (_options.CdataSectionElements is not { Count: > 0 } cdataElems)
            return false;
        var qualifiedName = string.IsNullOrEmpty(elemNs) ? elem.LocalName : $"Q{{{elemNs}}}{elem.LocalName}";
        if (cdataElems.Contains(qualifiedName))
            return true;
        // A bare local-name token in cdata-section-elements matches only no-namespace elements.
        if (string.IsNullOrEmpty(elemNs) && cdataElems.Contains(elem.LocalName))
            return true;
        return false;
    }

    private void WriteXhtmlAttributes(XdmElement elem, TextWriter output)
    {
        foreach (var attrId in elem.Attributes)
        {
            if (_store.GetNode(attrId) is not XdmAttribute attr)
                continue;
            output.Write(' ');
            output.Write(attr.LocalName);
            output.Write("=\"");
            var value = IsUriAttribute(elem.LocalName, attr.LocalName) && _options.EscapeUriAttributes
                ? EscapeUriAttributeValue(attr.Value)
                : attr.Value;
            output.Write(CharacterEscaper.EscapeXmlAttribute(value));
            output.Write('"');
        }
    }

}

/// <summary>
/// Output serialization method for <see cref="XQueryResultSerializer"/>.
/// </summary>
public enum OutputMethod
{
    /// <summary>
    /// Automatically choose XML for nodes, text for atomic values.
    /// </summary>
    Adaptive,

    /// <summary>
    /// Always serialize as XML.
    /// </summary>
    Xml,

    /// <summary>
    /// Always serialize as text (string values only).
    /// </summary>
    Text,

    /// <summary>
    /// Serialize as JSON.
    /// </summary>
    Json,

    /// <summary>
    /// Serialize as HTML.
    /// </summary>
    Html,

    /// <summary>
    /// Serialize as XHTML (XML well-formedness with HTML-compatibility rules).
    /// </summary>
    Xhtml
}

/// <summary>
/// Serialization options for XQuery output, corresponding to the
/// <c>declare option output:*</c> declarations in the XQuery prolog.
/// </summary>
public sealed record SerializationOptions
{
    /// <summary>
    /// The output method (xml, json, text, adaptive).
    /// </summary>
    public OutputMethod Method { get; init; } = OutputMethod.Adaptive;

    /// <summary>
    /// When <c>true</c> and the method is <see cref="OutputMethod.Adaptive"/>, a top-level
    /// atomic string is serialized quoted (<c>"simple string"</c>) per the W3C adaptive
    /// serialization method. Defaults to <c>false</c> so the string-in/string-out facade
    /// keeps returning bare strings; only the conformance harness opts in.
    /// </summary>
    public bool AdaptiveQuoteStrings { get; init; }

    /// <summary>
    /// Whether to indent the output for readability.
    /// </summary>
    public bool Indent { get; init; } = false;

    /// <summary>
    /// Whether to omit the XML declaration from XML output.
    /// </summary>
    public bool OmitXmlDeclaration { get; init; } = false;

    /// <summary>
    /// When <c>true</c>, the XML declaration (<c>&lt;?xml ...?&gt;</c>) is emitted even when the
    /// serialized item is a bare element rather than a document node. The XSLT/XQuery
    /// Serialization spec emits the declaration for the result regardless of whether the
    /// top-level item is a document node, unless <c>omit-xml-declaration</c> is <c>yes</c>;
    /// the engine's default (declaration only for document nodes) keeps the string-in/string-out
    /// facade clean, so this opt-in is set when <c>omit-xml-declaration</c> is explicitly
    /// <c>no</c> or a <c>standalone</c> value is requested (QT3 method-xml K2-Serialization-18/22/23).
    /// </summary>
    public bool ForceXmlDeclaration { get; init; } = false;

    /// <summary>
    /// Character encoding for the output (e.g., "UTF-8", "UTF-16").
    /// </summary>
    public string? Encoding { get; init; }

    /// <summary>
    /// Unicode normalization form applied to string output: <c>"NFC"</c>, <c>"NFD"</c>,
    /// <c>"NFKC"</c>, <c>"NFKD"</c>, or <c>"none"</c> (default). Per the serialization
    /// spec, character-map substitutions happen <em>after</em> normalization, so
    /// replacements themselves are not re-normalised.
    /// </summary>
    public string? NormalizationForm { get; init; }

    /// <summary>
    /// Output method used when serialising a node embedded in a JSON value (XSLT/XQuery
    /// Serialization §11.1). Defaults to <see cref="OutputMethod.Xml"/>; <c>"text"</c>
    /// yields the node's string-value only (no markup), and <c>"html"</c> uses the HTML
    /// output method. QT3 Serialization-json-52 covers <c>"text"</c>.
    /// </summary>
    public OutputMethod JsonNodeOutputMethod { get; init; } = OutputMethod.Xml;

    /// <summary>
    /// Standalone declaration: "yes", "no", or "omit".
    /// </summary>
    public string? Standalone { get; init; }

    /// <summary>
    /// System identifier for the DOCTYPE declaration.
    /// </summary>
    public string? DoctypeSystem { get; init; }

    /// <summary>
    /// Separator inserted between items in a sequence.
    /// </summary>
    public string? ItemSeparator { get; init; }

    /// <summary>
    /// Character maps for use-character-maps parameter.
    /// Keys are single characters, values are their replacement strings.
    /// </summary>
    public IDictionary<string, string>? CharacterMaps { get; init; }

    /// <summary>
    /// Whether to allow duplicate key names in JSON output.
    /// When true, duplicate keys are emitted as-is. When false (default), SERE0022 is raised.
    /// </summary>
    public bool AllowDuplicateNames { get; init; } = false;

    /// <summary>
    /// HTML version for the html output method (e.g., 5.0).
    /// </summary>
    public double? HtmlVersion { get; init; }

    /// <summary>
    /// Set of element QNames whose text content should be wrapped in CDATA sections.
    /// </summary>
    public ISet<string>? CdataSectionElements { get; init; }

    /// <summary>
    /// The XML version emitted in the XML declaration (<c>"1.0"</c> or <c>"1.1"</c>).
    /// Null means the serializer's default (<c>1.0</c>).
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Set of element QNames (in <c>Q{uri}local</c> or bare local-name form) whose immediate
    /// content must not be re-indented when <see cref="Indent"/> is on, per the
    /// <c>suppress-indentation</c> serialization parameter. Null means no suppression.
    /// </summary>
    public ISet<string>? SuppressIndentation { get; init; }

    /// <summary>
    /// The <c>doctype-public</c> serialization parameter (public identifier for the
    /// emitted DOCTYPE). Null means no public identifier was requested.
    /// </summary>
    public string? DoctypePublic { get; init; }

    /// <summary>
    /// The <c>include-content-type</c> serialization parameter (HTML/XHTML methods).
    /// When true (the default), a <c>&lt;meta&gt;</c> content-type declaration is injected
    /// into the <c>&lt;head&gt;</c> element. Defaults to true.
    /// </summary>
    public bool IncludeContentType { get; init; } = true;

    /// <summary>
    /// The <c>media-type</c> serialization parameter, used when injecting the HTML
    /// content-type <c>&lt;meta&gt;</c> element. Defaults to <c>text/html</c> for the HTML method.
    /// </summary>
    public string? MediaType { get; init; }

    /// <summary>
    /// The <c>escape-uri-attributes</c> serialization parameter (HTML/XHTML methods).
    /// When true (the default), the values of URI-valued attributes are %-escaped.
    /// </summary>
    public bool EscapeUriAttributes { get; init; } = true;

    /// <summary>
    /// Default serialization options (adaptive method, no indent).
    /// </summary>
    public static SerializationOptions Default { get; } = new();
}
