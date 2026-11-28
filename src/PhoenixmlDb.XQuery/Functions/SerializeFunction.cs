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
    private static readonly Serialize2Function Serialize2 = new();

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
        // F&O 3.1 §14.1.2: the one-argument form is the two-argument form with default
        // parameters, which are implementation-defined; like Saxon-HE 12.10 they are the xml
        // method's. It used the adaptive method: serialize((true(), "a")) gave `true() "a"`
        // where Saxon gives `true a`.
        return Serialize2.InvokeAsync([arguments[0], null], context);
    }

    internal static void CheckSerr0001(object? item, Ast.ExecutionContext? context = null)
    {
        if (item is Xdm.Nodes.XdmAttribute || item is Xdm.Nodes.XdmNamespace || item is XQueryFunction)
            throw new XQueryRuntimeException("SENR0001",
                "Attribute, namespace, or function item cannot be serialized at the top level");
    }

    internal static string SerializeNodeToXml(Xdm.Nodes.XdmNode node, INodeProvider? provider, Ast.ExecutionContext? context = null)
    {
        var sb = new StringBuilder();
        SerializeNodeToXml(node, provider, sb, isRoot: true);
        return sb.ToString();
    }

    private static void SerializeNodeToXml(Xdm.Nodes.XdmNode node, INodeProvider? provider, StringBuilder sb, Ast.ExecutionContext? context = null,
        Dictionary<string, string>? printedScope = null, bool isRoot = false)
    {
        // One call per level of the tree: on a deeply nested one, continue on a fresh stack.
        if (!Execution.StackGuard.HasRoom())
        {
            Execution.StackGuard.OnNewStack(() => SerializeNodeToXml(node, provider, sb, context, printedScope, isRoot));
            return;
        }
        switch (node)
        {
            case Xdm.Nodes.XdmDocument doc:
                foreach (var childId in doc.Children)
                    if (NodeNamespaces.NodeOf(node, childId, provider) is Xdm.Nodes.XdmNode childNode)
                        SerializeNodeToXml(childNode, provider, sb, printedScope: printedScope, isRoot: isRoot);
                break;
            case Xdm.Nodes.XdmElement elem:
                var prefix = elem.Prefix;
                var localName = elem.LocalName;
                var qname = !string.IsNullOrEmpty(prefix) ? $"{prefix}:{localName}" : localName;
                sb.Append('<').Append(qname);
                // Namespace declarations. Print a binding only when it differs from the one an ancestor
                // printed: copied descendants carry their inherited bindings, which must not repeat.
                Dictionary<string, string>? childScope = null;
                // The root declares its whole in-scope set: a parsed element records only its own xmlns attributes (#57).
                var declarations = isRoot && (provider != null || elem.Store != null)
                    ? NamespaceOutput.InScopeDeclarations(elem, id => NodeNamespaces.NodeOf(elem, id, provider))
                    : elem.NamespaceDeclarations;
                foreach (var nsDecl in declarations)
                {
                    if (PhoenixmlDb.XQuery.Execution.ElementConstructorOperator.IsNoInheritMarker(nsDecl)) continue;
                    var nsUri = NamespaceOutput.UriFor(nsDecl.Namespace, NodeNamespaces.UriOf(elem, nsDecl.Namespace, provider),
                        NamespaceOutput.DeclarationName(nsDecl.Prefix));
                    var declPrefix = nsDecl.Prefix ?? "";
                    string? printed = null;
                    printedScope?.TryGetValue(declPrefix, out printed);
                    // An undeclaration where no default was printed is a no-op.
                    if (printed == nsUri || (printed == null && declPrefix.Length == 0 && nsUri.Length == 0))
                        continue;
                    childScope ??= printedScope != null
                        ? new Dictionary<string, string>(printedScope, StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                    childScope[declPrefix] = nsUri;
                    if (string.IsNullOrEmpty(nsDecl.Prefix))
                        sb.Append(" xmlns=\"").Append(nsUri).Append('"');
                    else
                        sb.Append(" xmlns:").Append(nsDecl.Prefix).Append("=\"").Append(nsUri).Append('"');
                }
                // Namespace fixup for the element's own name and its prefixed attributes: the
                // binding in force must be the node's namespace, whatever the recorded declarations
                // say. A constructed element records its in-scope set, which need not mention a
                // default its parent printed, so an unprefixed no-namespace child under
                // <root xmlns="urn:x"> was written as <foo/> and read back in urn:x.
                void Fixup(string? fixPrefix, NamespaceId fixNs, string fixName)
                {
                    var key = fixPrefix ?? "";
                    var uri = NamespaceOutput.UriFor(fixNs, NodeNamespaces.UriOf(elem, fixNs, provider), fixName);
                    var scope = childScope ?? printedScope;
                    string? inForce = null;
                    scope?.TryGetValue(key, out inForce);
                    if ((inForce ?? "") == uri || (key.Length > 0 && uri.Length == 0))
                        return;
                    childScope = scope != null
                        ? new Dictionary<string, string>(scope, StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                    childScope[key] = uri;
                    sb.Append(' ').Append(NamespaceOutput.DeclarationName(fixPrefix)).Append("=\"").Append(uri).Append('"');
                }
                Fixup(elem.Prefix, elem.Namespace, qname);
                foreach (var attrId in elem.Attributes)
                    if (NodeNamespaces.NodeOf(node, attrId, provider) is Xdm.Nodes.XdmAttribute fixAttr && !string.IsNullOrEmpty(fixAttr.Prefix))
                        Fixup(fixAttr.Prefix, fixAttr.Namespace, fixAttr.Prefix + ":" + fixAttr.LocalName);
                // Attributes
                foreach (var attrId in elem.Attributes)
                    if (NodeNamespaces.NodeOf(node, attrId, provider) is Xdm.Nodes.XdmAttribute attr)
                    {
                        var attrName = !string.IsNullOrEmpty(attr.Prefix) ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName;
                        sb.Append(' ').Append(attrName).Append("=\"").Append(EscapeAttr(attr.Value)).Append('"');
                    }
                // Children
                var hasChildren = false;
                foreach (var childId in elem.Children)
                {
                    if (!hasChildren) { sb.Append('>'); hasChildren = true; }
                    if (NodeNamespaces.NodeOf(node, childId, provider) is Xdm.Nodes.XdmNode child)
                        SerializeNodeToXml(child, provider, sb, printedScope: childScope ?? printedScope);
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
}
