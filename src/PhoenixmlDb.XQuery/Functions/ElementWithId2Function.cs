using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:element-with-id($arg as xs:string*, $node as node()) as element()*</summary>
public sealed class ElementWithId2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "element-with-id");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "node"), Type = new() { ItemType = ItemType.Node, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        // element-with-id is like fn:id but only returns elements (not the parent of an xml:id attribute).
        // For DTD-validated documents, this behaves identically to fn:id.
        var store = context.NodeStore;
        var nodeArg = arguments[1];
        XdmDocument? doc = null;
        if (nodeArg is XdmDocument d)
            doc = d;
        else if (nodeArg is XdmNode n && store != null)
            doc = IdFunction.FindDocumentForNode(n, store);
        if (doc == null)
            throw new XQueryRuntimeException("FODC0001",
                "fn:element-with-id: node is not in a tree rooted at a document node");

        return ValueTask.FromResult<object?>(IdFunction.FindElementsWithId(arguments[0], doc, store!));
    }
}
