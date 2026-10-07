using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:idref($arg as xs:string*) as node()* (context item as node)</summary>
public sealed class Idref1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "idref");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var store = context.NodeStore;
        var contextItem = context.ContextItem;
        XdmDocument? doc = null;
        if (contextItem is XdmDocument d)
            doc = d;
        else if (contextItem is XdmNode n && store != null)
            doc = IdFunction.FindDocumentForNode(n, store);
        // An atomic context item is a type error, not "no document" (QT3 fn-idref-2).
        if (contextItem is not null and not XdmNode)
            throw new XQueryRuntimeException("XPTY0004",
                "fn:idref: the context item must be a node");
        if (doc == null)
            throw new XQueryRuntimeException("FODC0001",
                "fn:idref: context node is not in a tree rooted at a document node");

        return ValueTask.FromResult<object?>(IdrefFunction.FindNodesByIdref(arguments[0], doc, store!,
            (context as QueryExecutionContext)?.SchemaProvider));
    }
}
