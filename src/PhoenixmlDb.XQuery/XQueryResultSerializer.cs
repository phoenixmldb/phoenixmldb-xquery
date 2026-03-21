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
/// For the simplest usage, call the static <see cref="Serialize(object?, XdmDocumentStore)"/> method.
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

    /// <summary>
    /// Creates a new serializer backed by the given document store.
    /// </summary>
    /// <param name="store">The document store used to resolve child nodes and namespaces during serialization.</param>
    /// <param name="method">The output method. Defaults to <see cref="OutputMethod.Adaptive"/>.</param>
    public XQueryResultSerializer(XdmDocumentStore store, OutputMethod method = OutputMethod.Adaptive)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _method = method;
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
    /// <returns>The serialized string representation.</returns>
    public static string Serialize(object? item, XdmDocumentStore store)
    {
        var serializer = new XQueryResultSerializer(store);
        return serializer.Serialize(item);
    }

    private void SerializeTo(object? item, TextWriter output)
    {
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
            Indent = true,
            OmitXmlDeclaration = node is not XdmDocument,
            Encoding = Encoding.UTF8,
            ConformanceLevel = node is XdmDocument ? ConformanceLevel.Document : ConformanceLevel.Fragment
        };

        using var writer = XmlWriter.Create(output, settings);
        WriteNode(writer, node);
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
                if (!string.IsNullOrEmpty(elem.Prefix))
                    writer.WriteStartElement(elem.Prefix, elem.LocalName, ns);
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
    Text
}
