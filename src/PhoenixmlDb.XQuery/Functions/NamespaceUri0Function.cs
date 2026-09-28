using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:namespace-uri() as xs:anyURI (uses context item)
/// </summary>
public sealed class NamespaceUri0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "namespace-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var item = context.ContextItem;
        if (item == null)
            throw new XQueryRuntimeException("XPDY0002", "Context item is absent in fn:namespace-uri()");

        var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
        // xs:anyURI, like the one-argument form (xquery#83).
        return ValueTask.FromResult<object?>(new Xdm.XsAnyUri(item switch
        {
            XdmElement elem => NamespaceUriFunction.ResolveNsId(elem.Namespace, qec),
            XdmAttribute attr => NamespaceUriFunction.ResolveNsId(attr.Namespace, qec),
            XdmNamespace => "",
            XdmDocument or XdmText or XdmComment or XdmProcessingInstruction or TextNodeItem => "",
            _ => throw new XQueryRuntimeException("XPTY0004", "Context item is not a node in fn:namespace-uri()")
        }));
    }
}
