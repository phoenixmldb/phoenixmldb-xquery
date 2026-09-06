using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:resolve-QName($qname, $element) as xs:QName?
/// Resolves a QName string using the in-scope namespaces of an element.
/// </summary>
public sealed class ResolveQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "resolve-QName");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "qname"), Type = XdmSequenceType.OptionalString },
            new() { Name = new QName(NamespaceId.None, "element"), Type = new XdmSequenceType { ItemType = ItemType.Element, Occurrence = Occurrence.ExactlyOne } }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var qnameArg = arguments[0];
        if (qnameArg == null) return ValueTask.FromResult<object?>(null);

        var qnameStr = (qnameArg.ToString() ?? "").Trim();
        var elementArg = arguments[1];

        // Split the QName into prefix and local parts
        var colonIdx = qnameStr.IndexOf(':', StringComparison.Ordinal);
        string? prefix;
        string localName;
        if (colonIdx >= 0)
        {
            prefix = qnameStr[..colonIdx];
            localName = qnameStr[(colonIdx + 1)..];
            // Validate: no second colon allowed
            if (localName.Contains(':', StringComparison.Ordinal))
                throw new InvalidOperationException($"FOCA0002: '{qnameStr}' is not a valid QName");
        }
        else
        {
            prefix = null;
            localName = qnameStr;
        }

        // Validate NCName parts
        if ((prefix != null && !IsValidNCName(prefix)) || !IsValidNCName(localName))
            throw new InvalidOperationException($"FOCA0002: '{qnameStr}' is not a valid QName");

        // Handle XdmElement (from RTFs and XDM node store)
        if (elementArg is XdmElement xdmElem)
        {
            Func<NamespaceId, string?>? nsResolver = null;
            if (context is Execution.QueryExecutionContext qec)
                nsResolver = qec.NamespaceResolver;

            string namespaceUri;
            if (prefix == "xml")
            {
                namespaceUri = "http://www.w3.org/XML/1998/namespace";
            }
            else if (prefix != null)
            {
                // Search in-scope namespace declarations (element + ancestors) for the prefix
                string? found = null;
                var nodeStore = context.NodeStore;
                XdmNode? current = xdmElem;
                while (current != null && found == null)
                {
                    if (current is XdmElement elem2)
                    {
                        foreach (var nsDecl in elem2.NamespaceDeclarations)
                        {
                            if (nsDecl.Prefix == prefix)
                            {
                                found = nsResolver?.Invoke(nsDecl.Namespace) ?? "";
                                break;
                            }
                        }
                    }
                    current = current.Parent.HasValue && nodeStore != null
                        ? nodeStore.GetNode(current.Parent.Value) as XdmNode : null;
                }
                if (found == null)
                    throw new Execution.XQueryRuntimeException("FONS0004", $"Prefix '{prefix}' is not declared in the in-scope namespaces");
                namespaceUri = found;
            }
            else
            {
                // No prefix: use the default namespace (element + ancestors)
                namespaceUri = "";
                var nodeStore2 = context.NodeStore;
                XdmNode? current2 = xdmElem;
                while (current2 != null)
                {
                    if (current2 is XdmElement elem3)
                    {
                        foreach (var nsDecl in elem3.NamespaceDeclarations)
                        {
                            if (string.IsNullOrEmpty(nsDecl.Prefix))
                            {
                                namespaceUri = nsResolver?.Invoke(nsDecl.Namespace) ?? "";
                                goto foundDefault;
                            }
                        }
                    }
                    current2 = current2.Parent.HasValue && nodeStore2 != null
                        ? nodeStore2.GetNode(current2.Parent.Value) as XdmNode : null;
                }
                foundDefault:;
            }

            // Do NOT set ExpandedNamespace when a prefix is present: QName.ToString() uses
            // Q{uri}local notation only when ExpandedNamespace is set and there is no prefix,
            // which would produce the wrong serialization (e.g., "Q{http://...}local" instead of
            // "ns:local"). Use RuntimeNamespace for namespace-aware comparison without affecting display.
            return ValueTask.FromResult<object?>(new QName(NamespaceId.None, localName, prefix) { RuntimeNamespace = namespaceUri });
        }

        // Handle System.Xml.Linq.XElement (from source documents)
        if (elementArg is not System.Xml.Linq.XElement element)
        {
            if (elementArg is System.Xml.Linq.XObject xobj && xobj.Parent is System.Xml.Linq.XElement parentEl)
                element = parentEl;
            else
                throw new InvalidOperationException("FORG0006: Second argument to resolve-QName must be an element");
        }

        // Resolve the namespace using the element's in-scope namespaces
        string nsUri;
        if (prefix != null)
        {
            var ns = element.GetNamespaceOfPrefix(prefix);
            if (ns == null)
                throw new InvalidOperationException($"FONS0004: Prefix '{prefix}' is not declared in the in-scope namespaces");
            nsUri = ns.NamespaceName;
        }
        else
        {
            // No prefix: use the default namespace
            nsUri = element.GetDefaultNamespace().NamespaceName;
        }

        // When a prefix is present use RuntimeNamespace for comparison only (not ExpandedNamespace,
        // which would change ToString() to Q{uri}local instead of prefix:local).
        return ValueTask.FromResult<object?>(prefix != null
            ? new QName(NamespaceId.None, localName, prefix) { RuntimeNamespace = nsUri }
            : new QName(NamespaceId.None, localName, null) { ExpandedNamespace = string.IsNullOrEmpty(nsUri) ? null : nsUri });
    }

    private static bool IsValidNCName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var first = name[0];
        if (first != '_' && !char.IsLetter(first)) return false;
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_' && !char.IsControl(c)
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.SpacingCombiningMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.EnclosingMark)
                return false;
        }
        return true;
    }
}
