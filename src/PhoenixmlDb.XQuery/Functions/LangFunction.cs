using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:lang($testlang as xs:string?, $node as node()) as xs:boolean</summary>
public sealed class LangFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "lang");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "testlang"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "node"), Type = new() { ItemType = ItemType.Node, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var testLang = arguments[0]?.ToString();
        if (testLang == null) return ValueTask.FromResult<object?>(false);
        var node = arguments[1];
        if (node == null) return ValueTask.FromResult<object?>(false);
        if (node is not XdmNode)
            throw new XQueryRuntimeException("XPTY0004",
                $"Second argument to fn:lang must be a node, got {node.GetType().Name}");

        // The tree is read in the node's own store, the context's for a node no store holds.
        var nodeStore = context.NodeStore;

        // Walk up ancestors looking for xml:lang attribute
        var current = node as XdmNode;
        while (current != null)
        {
            if (current is XdmElement elem)
            {
                foreach (var attrId in elem.Attributes)
                {
                    if (Execution.NodeNamespaces.NodeOf(elem, attrId, nodeStore) is not XdmAttribute attr)
                        continue;
                    var nsUri = Execution.NodeNamespaces.UriOf(attr, attr.Namespace, context);
                    if (attr.LocalName == "lang" &&
                        (nsUri == "http://www.w3.org/XML/1998/namespace" || attr.Prefix == "xml"))
                    {
                        var langVal = attr.StringValue;
                        // Match per BCP 47: testLang matches if langVal equals testLang
                        // or starts with testLang followed by '-'
                        if (langVal.Equals(testLang, StringComparison.OrdinalIgnoreCase) ||
                            langVal.StartsWith(testLang + "-", StringComparison.OrdinalIgnoreCase))
                            return ValueTask.FromResult<object?>(true);
                        return ValueTask.FromResult<object?>(false);
                    }
                }
            }
            if (current.Parent.HasValue)
                current = Execution.NodeNamespaces.NodeOf(current, current.Parent.Value, nodeStore);
            else
                break;
        }
        return ValueTask.FromResult<object?>(false);
    }
}
