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

            // Get namespaces from the element's declarations
            foreach (var binding in elem.NamespaceDeclarations)
            {
                // Resolve NamespaceId to URI string
                var nsUri = PhoenixmlDb.XQuery.Functions.FunctionNamespaces.ResolveNamespace(binding.Namespace);
                if (nsUri != null)
                    result[binding.Prefix] = new PhoenixmlDb.Xdm.XsAnyUri(nsUri);
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}
