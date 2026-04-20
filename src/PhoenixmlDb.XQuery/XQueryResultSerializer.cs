using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
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

        // Normalize self-closing tags: .NET's XmlWriter emits "<elem />" but the spec expects "<elem/>"
        result = Regex.Replace(result, @" />", "/>");

        // Apply character maps if present
        if (_options.CharacterMaps is { Count: > 0 })
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
                    "xhtml" => OutputMethod.Xml,
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
            if (cse is object?[] cseArr)
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
            CdataSectionElements = cdataSectionElements
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
        // SEPM0004: standalone=yes or doctype-system requires a well-formed document
        if (_method == OutputMethod.Xml
            && (_options.Standalone is "yes" || _options.DoctypeSystem != null)
            && item is not XdmDocument)
        {
            throw new XQueryRuntimeException("SEPM0004",
                "Serialization error: standalone or doctype-system requires a well-formed XML document");
        }

        if (_method == OutputMethod.Json)
        {
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
                    output.Write($"{attr.LocalName}=\"{EscapeXmlAttribute(attr.Value)}\"");
                else if (_method == OutputMethod.Xml)
                    output.Write($"{attr.LocalName}=\"{EscapeXmlAttribute(attr.Value)}\"");
                else
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

            case IDictionary<object, object?> map when _method == OutputMethod.Adaptive:
                SerializeMapAdaptive(map, output);
                break;

            case IDictionary<object, object?> map:
                SerializeMapAsJson(map, output);
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
                if (_method == OutputMethod.Adaptive)
                    output.Write(b ? "true()" : "false()");
                else
                    output.Write(b ? "true" : "false");
                break;

            case double d:
                output.Write(Functions.ConcatFunction.XQueryStringValue(d));
                break;

            case float f:
                output.Write(Functions.ConcatFunction.XQueryStringValue(f));
                break;

            default:
                output.Write(item.ToString());
                break;
        }
    }

    /// <summary>
    /// Serializes a map in adaptive output format: map{key:value,key:value}
    /// </summary>
    private void SerializeMapAdaptive(IDictionary<object, object?> map, TextWriter output)
    {
        output.Write("map{");
        var first = true;
        foreach (var (key, value) in map)
        {
            if (!first) output.Write(',');
            first = false;
            // Key serialization in adaptive mode
            SerializeTo(key, output);
            output.Write(':');
            SerializeTo(value, output);
        }
        output.Write('}');
    }

    private void SerializeXmlNode(XdmNode node, TextWriter output)
    {
        var settings = new XmlWriterSettings
        {
            Indent = _options.Indent,
            OmitXmlDeclaration = _options.OmitXmlDeclaration || node is not XdmDocument,
            Encoding = _options.Encoding != null
                ? System.Text.Encoding.GetEncoding(_options.Encoding)
                : Encoding.UTF8,
            ConformanceLevel = node is XdmDocument ? ConformanceLevel.Document : ConformanceLevel.Fragment,
            NewLineHandling = NewLineHandling.Entitize
        };

        using var writer = XmlWriter.Create(output, settings);

        if (node is XdmDocument && !_options.OmitXmlDeclaration)
        {
            if (_options.Standalone is "yes")
                writer.WriteStartDocument(true);
            else if (_options.Standalone is "no")
                writer.WriteStartDocument(false);
            else
                writer.WriteStartDocument();

            foreach (var childId in ((XdmDocument)node).Children)
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
    /// Writes an attribute value, escaping CR/LF/TAB as character references per the
    /// XML serialization spec. XmlWriter.WriteAttributeString normalizes these characters
    /// (replacing with spaces or dropping them), so we use WriteRaw for segments that
    /// contain these characters.
    /// </summary>
    private static void WriteAttributeValueEscaped(XmlWriter writer, string value)
    {
        if (value.AsSpan().IndexOfAny('\r', '\n', '\t') < 0)
        {
            writer.WriteString(value);
            return;
        }

        var span = value.AsSpan();
        var start = 0;
        for (var i = 0; i < span.Length; i++)
        {
            string? replacement = span[i] switch
            {
                '\r' => "&#xD;",
                '\n' => "&#xA;",
                '\t' => "&#x9;",
                _ => null
            };
            if (replacement != null)
            {
                if (i > start)
                    writer.WriteString(span[start..i].ToString());
                writer.WriteRaw(replacement);
                start = i + 1;
            }
        }
        if (start < span.Length)
            writer.WriteString(span[start..].ToString());
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

                // Write children
                foreach (var childId in elem.Children)
                {
                    var child = _store.GetNode(childId);
                    if (child != null)
                        WriteNode(writer, child, isCdataElem);
                }

                writer.WriteEndElement();
                break;

            case XdmText text:
                if (_options.CdataSectionElements is { Count: > 0 } && inCdataElement)
                    writer.WriteCData(text.Value);
                else
                    writer.WriteString(text.Value);
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
            case int or long or double or float or decimal or BigInteger:
                output.Write(Convert.ToString(item, CultureInfo.InvariantCulture));
                break;
            case string s:
                output.Write('"');
                output.Write(EscapeJsonString(s));
                output.Write('"');
                break;
            case IDictionary<object, object?> nestedMap:
                SerializeMapAsJson(nestedMap, output, depth);
                break;
            case object?[] array:
                output.Write('[');
                for (int i = 0; i < array.Length; i++)
                {
                    if (i > 0) output.Write(',');
                    output.Write(newline);
                    if (indent) output.Write(innerIndent);
                    SerializeAsJson(array[i], output, depth + 1);
                }
                if (array.Length > 0)
                {
                    output.Write(newline);
                    if (indent) output.Write(outerIndent);
                }
                output.Write(']');
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
        // For text nodes, just return the text value
        if (node is XdmText text)
            return text.Value;

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

        // For elements and other nodes, serialize as XML fragment
        var xmlOptions = new SerializationOptions
        {
            Method = OutputMethod.Xml,
            OmitXmlDeclaration = true
        };
        var xmlSerializer = new XQueryResultSerializer(_store, xmlOptions);
        return xmlSerializer.Serialize(node);
    }

    private string EscapeJsonString(string s)
    {
        var sb = new StringBuilder(s.Length);
        var targetEncoding = _options.Encoding != null
            ? System.Text.Encoding.GetEncoding(_options.Encoding)
            : null;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
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

    // HTML void elements that must not have a closing tag
    private static readonly HashSet<string> HtmlVoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr"
    };

    /// <summary>
    /// Serializes a node as HTML output, emitting DOCTYPE, meta charset, void elements, etc.
    /// </summary>
    private void SerializeAsHtml(XdmNode node, TextWriter output)
    {
        if (node is XdmDocument doc)
        {
            // Emit HTML5 DOCTYPE
            output.Write("<!DOCTYPE html>");
            if (_options.Indent) output.WriteLine();
            foreach (var childId in doc.Children)
            {
                var child = _store.GetNode(childId);
                if (child is XdmNode childNode)
                    WriteHtmlNode(childNode, output);
            }
        }
        else if (node is XdmElement elem)
        {
            // For top-level element, check if it's <html> — emit DOCTYPE
            if (elem.LocalName.Equals("html", StringComparison.OrdinalIgnoreCase))
            {
                output.Write("<!DOCTYPE html>");
                if (_options.Indent) output.WriteLine();
            }
            WriteHtmlNode(elem, output);
        }
    }

    private void WriteHtmlNode(XdmNode node, TextWriter output)
    {
        switch (node)
        {
            case XdmElement elem:
                var localName = elem.LocalName;
                output.Write('<');
                output.Write(localName);

                // Write attributes
                foreach (var attrId in elem.Attributes)
                {
                    if (_store.GetNode(attrId) is XdmAttribute attr)
                    {
                        output.Write(' ');
                        output.Write(attr.LocalName);
                        output.Write("=\"");
                        output.Write(EscapeXmlAttribute(attr.Value));
                        output.Write('"');
                    }
                }

                output.Write('>');

                // Inject meta charset into <head> if not already present
                if (localName.Equals("head", StringComparison.OrdinalIgnoreCase))
                {
                    var enc = _options.Encoding ?? "UTF-8";
                    output.Write($"<meta charset=\"{enc}\">");
                }

                // Write children
                foreach (var childId in elem.Children)
                {
                    var child = _store.GetNode(childId);
                    if (child is XdmNode childNode)
                        WriteHtmlNode(childNode, output);
                }

                // Void elements: no closing tag
                if (!HtmlVoidElements.Contains(localName))
                {
                    output.Write("</");
                    output.Write(localName);
                    output.Write('>');
                }
                break;

            case XdmText text:
                output.Write(text.Value);
                break;

            case XdmComment comment:
                output.Write("<!--");
                output.Write(comment.Value);
                output.Write("-->");
                break;

            case XdmProcessingInstruction pi:
                output.Write("<?");
                output.Write(pi.Target);
                output.Write(' ');
                output.Write(pi.Value);
                output.Write('>');
                break;
        }
    }

    private static string EscapeXmlAttribute(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
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
    Html
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
    /// Whether to indent the output for readability.
    /// </summary>
    public bool Indent { get; init; } = false;

    /// <summary>
    /// Whether to omit the XML declaration from XML output.
    /// </summary>
    public bool OmitXmlDeclaration { get; init; } = false;

    /// <summary>
    /// Character encoding for the output (e.g., "UTF-8", "UTF-16").
    /// </summary>
    public string? Encoding { get; init; }

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
    /// Default serialization options (adaptive method, no indent).
    /// </summary>
    public static SerializationOptions Default { get; } = new();
}
