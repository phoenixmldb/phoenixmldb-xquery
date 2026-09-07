using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:serialize($arg) as xs:string
/// </summary>
public sealed class SerializeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "serialize");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>("");

        // SENR0001: top-level attribute/namespace/function item cannot be serialized
        CheckSerr0001(arg);
        if (arg is IEnumerable<object?> seq && arg is not string)
            foreach (var it in seq) CheckSerr0001(it);

        var nodeProvider = (context as QueryExecutionContext)?.NodeProvider;
        // XPath/XQuery 3.1 §17.1.3: the 1-arg form uses the default serialization
        // parameters, which include `method=adaptive`.
        var result = SerializeItem(arg, nodeProvider, OutputMethod.Adaptive);
        return ValueTask.FromResult<object?>(result);
    }

    internal static void CheckSerr0001(object? item, Ast.ExecutionContext? context = null)
    {
        if (item is Xdm.Nodes.XdmAttribute || item is Xdm.Nodes.XdmNamespace || item is XQueryFunction)
            throw new XQueryRuntimeException("SENR0001",
                "Attribute, namespace, or function item cannot be serialized at the top level");
    }

    internal static string SerializeItem(object? item, INodeProvider? nodeProvider,
        OutputMethod method = OutputMethod.Json)
    {
        if (item == null) return "";
        if (method == OutputMethod.Adaptive)
            return SerializeItemAdaptive(item, nodeProvider);
        return item switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            Xdm.Nodes.XdmNode node => SerializeNodeToXml(node, nodeProvider),
            IDictionary<object, object?> map => SerializeMapAsJson(map),
            List<object?> array => SerializeArrayAsJson(array),
            object?[] arr => string.Join(" ", arr.Where(x => x != null).Select(x => SerializeItem(x, nodeProvider))),
            _ => item.ToString() ?? ""
        };
    }

    /// <summary>
    /// Adaptive serialization per XPath/XQuery 3.1 §27.7. Each item is serialized in a
    /// kind-specific form: nodes as XML markup, maps as <c>map{…}</c>, arrays as
    /// <c>[…]</c>, atomic values as constructor-form. Used by <see cref="Serialize2Function"/>
    /// when the engine isn't running against an <see cref="XdmDocumentStore"/> (e.g. when
    /// fn:serialize is invoked from XSLT, which uses its own in-memory node store).
    /// </summary>
    private static string SerializeItemAdaptive(object? item, INodeProvider? nodeProvider, Ast.ExecutionContext? context = null)
    {
        var sb = new StringBuilder();
        AppendAdaptive(item, nodeProvider, sb);
        return sb.ToString();
    }

    private static void AppendAdaptive(object? item, INodeProvider? np, StringBuilder sb, Ast.ExecutionContext? context = null)
    {
        switch (item)
        {
            case null:
                return;
            case Xdm.Nodes.XdmNode node:
                sb.Append(SerializeNodeToXml(node, np));
                return;
            case IDictionary<object, object?> map:
                sb.Append("map{");
                var firstM = true;
                foreach (var (k, v) in map)
                {
                    if (!firstM) sb.Append(',');
                    firstM = false;
                    AppendAdaptiveAtomic(k, sb);
                    sb.Append(':');
                    AppendAdaptive(v, np, sb);
                }
                sb.Append('}');
                return;
            case List<object?> array:
                sb.Append('[');
                for (int i = 0; i < array.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendAdaptive(array[i], np, sb);
                }
                sb.Append(']');
                return;
            case object?[] seq:
                // XPath/XQuery 3.1 §27.7: items in a sequence are space-separated in
                // adaptive output (no surrounding parens).
                for (int i = 0; i < seq.Length; i++)
                {
                    if (i > 0) sb.Append(' ');
                    AppendAdaptive(seq[i], np, sb);
                }
                return;
            case IEnumerable<object?> enumerable when item is not string:
                bool firstE = true;
                foreach (var e in enumerable)
                {
                    if (!firstE) sb.Append(' ');
                    firstE = false;
                    AppendAdaptive(e, np, sb);
                }
                return;
            default:
                AppendAdaptiveAtomic(item, sb);
                return;
        }
    }

    private static void AppendAdaptiveAtomic(object? item, StringBuilder sb, Ast.ExecutionContext? context = null)
    {
        switch (item)
        {
            case null: return;
            case string s:
                sb.Append('"');
                foreach (var c in s)
                    sb.Append(c == '"' ? "\"\"" : c.ToString());
                sb.Append('"');
                return;
            case bool b:
                sb.Append(b ? "true()" : "false()");
                return;
            case int or long or short or byte or sbyte or uint or ulong or ushort:
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}", item));
                return;
            case double d:
                sb.Append(ConcatFunction.XQueryStringValue(d));
                return;
            case float f:
                sb.Append(ConcatFunction.XQueryStringValue(f));
                return;
            case decimal dec:
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}", dec));
                return;
            default:
                sb.Append('"').Append(item.ToString() ?? "").Append('"');
                return;
        }
    }

    internal static string SerializeNodeToXml(Xdm.Nodes.XdmNode node, INodeProvider? provider, Ast.ExecutionContext? context = null)
    {
        var sb = new StringBuilder();
        SerializeNodeToXml(node, provider, sb);
        return sb.ToString();
    }

    private static void SerializeNodeToXml(Xdm.Nodes.XdmNode node, INodeProvider? provider, StringBuilder sb, Ast.ExecutionContext? context = null)
    {
        switch (node)
        {
            case Xdm.Nodes.XdmDocument doc:
                foreach (var childId in doc.Children)
                    if (provider?.GetNode(childId) is Xdm.Nodes.XdmNode childNode)
                        SerializeNodeToXml(childNode, provider, sb);
                break;
            case Xdm.Nodes.XdmElement elem:
                var prefix = elem.Prefix;
                var localName = elem.LocalName;
                var qname = !string.IsNullOrEmpty(prefix) ? $"{prefix}:{localName}" : localName;
                sb.Append('<').Append(qname);
                // Namespace declarations
                foreach (var nsDecl in elem.NamespaceDeclarations)
                {
                    // Resolve namespace URI via the provider if possible
                    var nsUri = "";
                    if (provider is XdmDocumentStore store)
                        nsUri = store.ResolveNamespaceUri(nsDecl.Namespace)?.ToString() ?? "";
                    if (string.IsNullOrEmpty(nsDecl.Prefix))
                        sb.Append(" xmlns=\"").Append(nsUri).Append('"');
                    else
                        sb.Append(" xmlns:").Append(nsDecl.Prefix).Append("=\"").Append(nsUri).Append('"');
                }
                // Attributes
                foreach (var attrId in elem.Attributes)
                    if (provider?.GetNode(attrId) is Xdm.Nodes.XdmAttribute attr)
                    {
                        var attrName = !string.IsNullOrEmpty(attr.Prefix) ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName;
                        sb.Append(' ').Append(attrName).Append("=\"").Append(EscapeAttr(attr.Value)).Append('"');
                    }
                // Children
                var hasChildren = false;
                foreach (var childId in elem.Children)
                {
                    if (!hasChildren) { sb.Append('>'); hasChildren = true; }
                    if (provider?.GetNode(childId) is Xdm.Nodes.XdmNode child)
                        SerializeNodeToXml(child, provider, sb);
                }
                if (!hasChildren)
                    sb.Append("/>");
                else
                    sb.Append("</").Append(qname).Append('>');
                break;
            case Xdm.Nodes.XdmText text:
                sb.Append(System.Security.SecurityElement.Escape(text.Value));
                break;
            case Xdm.Nodes.XdmComment comment:
                sb.Append("<!--").Append(comment.Value).Append("-->");
                break;
            case Xdm.Nodes.XdmProcessingInstruction pi:
                sb.Append("<?").Append(pi.Target);
                if (!string.IsNullOrEmpty(pi.Value))
                    sb.Append(' ').Append(pi.Value);
                sb.Append("?>");
                break;
            default:
                sb.Append(node.StringValue);
                break;
        }
    }

    private static string EscapeAttr(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");

    private static string SerializeMapAsJson(IDictionary<object, object?> map, Ast.ExecutionContext? context = null)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var (key, value) in map)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(JsonEscape(key.ToString() ?? "")).Append("\":");
            SerializeJsonValue(value, sb);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string SerializeArrayAsJson(List<object?> array, Ast.ExecutionContext? context = null)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < array.Count; i++)
        {
            if (i > 0) sb.Append(',');
            SerializeJsonValue(array[i], sb);
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static void SerializeJsonValue(object? value, StringBuilder sb, Ast.ExecutionContext? context = null)
    {
        switch (value)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case int or long or double or float or decimal:
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}", value)); break;
            case string s: sb.Append('"').Append(JsonEscape(s)).Append('"'); break;
            case IDictionary<object, object?> m: sb.Append(SerializeMapAsJson(m)); break;
            case List<object?> a: sb.Append(SerializeArrayAsJson(a)); break;
            case object?[] arr:
                sb.Append('[');
                for (int i = 0; i < arr.Length; i++) { if (i > 0) sb.Append(','); SerializeJsonValue(arr[i], sb); }
                sb.Append(']'); break;
            default: sb.Append('"').Append(JsonEscape(value.ToString() ?? "")).Append('"'); break;
        }
    }

    private static string JsonEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("/", "\\/").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
