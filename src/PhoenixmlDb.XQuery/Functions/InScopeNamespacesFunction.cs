using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:in-scope-namespaces($element as element()) as map(xs:string, xs:anyURI)
/// Returns a map of prefix → namespace URI for all in-scope namespaces (XPath 4.0).
/// </summary>
public sealed class InScopeNamespacesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "in-scope-namespaces");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "element"), Type = new() { ItemType = ItemType.Element, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var result = new OrderedXdmMap(XdmMapKeyComparer.Instance);

        if (arguments[0] is PhoenixmlDb.Xdm.Nodes.XdmElement elem)
        {
            // Add xml namespace (always in scope)
            result["xml"] = new PhoenixmlDb.Xdm.XsAnyUri("http://www.w3.org/XML/1998/namespace");

            // The in-scope bindings, through the SAME shared walk the namespace axis,
            // in-scope-prefixes and namespace-uri-for-prefix use, so the four cannot disagree.
            // This used to read only the element's OWN declarations and resolve them through the
            // static well-known table, so a binding declared on an ancestor — or any namespace
            // interned by the document or constructor rather than predefined — was missing.
            var qec = context as QueryExecutionContext;
            var nodeStore = context.NodeStore;
            foreach (var (prefix, nsId) in AxisNavigationOperator.GatherInScopeNamespaces(
                elem, id => nodeStore?.GetNode(id) as XdmNode))
            {
                if (prefix == "xml")
                    continue;
                // An unprefixed element in no namespace proves there is no in-scope default; the walk
                // can still surface an ancestor's default across an xmlns="" undeclaration
                // (namespace-uri-for-prefix settles the same case the same way).
                if (prefix.Length == 0 && elem.Namespace == NamespaceId.None)
                    continue;
                var nsUri = NamespaceUriFunction.ResolveNsId(nsId, qec);
                if (string.IsNullOrEmpty(nsUri))
                    continue;
                result[prefix] = new PhoenixmlDb.Xdm.XsAnyUri(nsUri);
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}
