using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml;
using PhoenixmlDb.Xdm.Nodes;

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
        using var sw = new StringWriter();
        SerializeTo(item, sw);
        return sw.ToString();
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

    private void SerializeTo(object? item, TextWriter output)
    {
        if (_method == OutputMethod.Json)
        {
            SerializeAsJson(item, output);
            return;
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
                if (_method == OutputMethod.Xml)
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

            case IDictionary<object, object?> map:
                SerializeMapAsJson(map, output);
                break;

            case List<object?> xdmArray:
                // XDM array — serialize as JSON array in adaptive mode
                SerializeArrayAsJson(xdmArray, output);
                break;

            case object?[] array:
                var first = true;
                foreach (var element in array)
                {
                    if (!first && _method == OutputMethod.Text)
                        output.Write(' ');
                    SerializeTo(element, output);
                    first = false;
                }
                break;

            case IEnumerable<object?> sequence:
                var isFirst = true;
                foreach (var element in sequence)
                {
                    if (!isFirst && _method == OutputMethod.Text)
                        output.Write(' ');
                    SerializeTo(element, output);
                    isFirst = false;
                }
                break;

            default:
                output.Write(item.ToString());
                break;
        }
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
            ConformanceLevel = node is XdmDocument ? ConformanceLevel.Document : ConformanceLevel.Fragment
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

    private void WriteNode(XmlWriter writer, XdmNode node)
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
                else if (!string.IsNullOrEmpty(ns))
                    writer.WriteStartElement(elem.LocalName, ns);
                else
                    writer.WriteStartElement(elem.LocalName);

                // Write namespace declarations
                foreach (var nsDecl in elem.NamespaceDeclarations)
                {
                    var declUri = _store.ResolveNamespaceUri(nsDecl.Namespace)?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(nsDecl.Prefix))
                        writer.WriteAttributeString("xmlns", declUri);
                    else
                        writer.WriteAttributeString("xmlns", nsDecl.Prefix, null, declUri);
                }

                // Write attributes
                foreach (var attrId in elem.Attributes)
                {
                    if (_store.GetNode(attrId) is XdmAttribute attr)
                    {
                        var attrNs = _store.ResolveNamespaceUri(attr.Namespace)?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(attr.Prefix))
                            writer.WriteAttributeString(attr.Prefix, attr.LocalName, attrNs, attr.Value);
                        else if (!string.IsNullOrEmpty(attrNs))
                            writer.WriteAttributeString(attr.LocalName, attrNs, attr.Value);
                        else
                            writer.WriteAttributeString(attr.LocalName, attr.Value);
                    }
                }

                // Write children
                foreach (var childId in elem.Children)
                {
                    var child = _store.GetNode(childId);
                    if (child != null)
                        WriteNode(writer, child);
                }

                writer.WriteEndElement();
                break;

            case XdmText text:
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
                output.Write('"');
                output.Write(EscapeJsonString(node.StringValue ?? ""));
                output.Write('"');
                break;
            default:
                output.Write('"');
                output.Write(EscapeJsonString(item.ToString() ?? ""));
                output.Write('"');
                break;
        }
    }

    private static string EscapeJsonString(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
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
    Json
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
    /// Default serialization options (adaptive method, no indent).
    /// </summary>
    public static SerializationOptions Default { get; } = new();
}
