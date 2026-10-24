using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:id($arg as xs:string*, $node as node()) as element()* — 2-arg version that uses
/// the specified node's document tree.
/// </summary>
public sealed class Id2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "id");
    public override XdmSequenceType ReturnType => new()
    {
        ItemType = ItemType.Element,
        Occurrence = Occurrence.ZeroOrMore
    };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType
            { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore } },
        new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.Node }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var store = context.NodeStore;
        var nodeArg = arguments[1];
        // $node is node(): an atomic value is XPTY0004, not FODC0001 (QT3 fn-id-22, K2-SeqIDFunc-2).
        if (nodeArg is not null and not XdmNode)
            throw context.Error("XPTY0004", "The second argument of fn:id must be a node");
        XdmDocument? doc = null;
        if (nodeArg is XdmDocument d)
            doc = d;
        else if (nodeArg is XdmNode n && store != null)
            doc = IdFunction.FindDocumentForNode(n, store);
        if (doc == null)
            throw context.Error("FODC0001", "FODC0001: No context document for fn:id");

        return ValueTask.FromResult<object?>(IdFunction.FindElementsById(arguments[0], doc, store!));
    }
}
