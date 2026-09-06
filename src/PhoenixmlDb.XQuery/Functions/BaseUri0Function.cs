using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:base-uri() as xs:anyURI? (uses context item)
/// </summary>
public sealed class BaseUri0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "base-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is not Execution.QueryExecutionContext qec)
            return ValueTask.FromResult<object?>(null);
        var item = qec.ContextItem;
        if (item is not XdmNode node)
        {
            // Per XQuery spec: if the context item is not a node, raise XPTY0004
            if (item != null)
                throw new XQueryRuntimeException("XPTY0004",
                    "Context item for fn:base-uri() is not a node");
            return ValueTask.FromResult<object?>(null);
        }
        var uri = BaseUriFunction.ComputeBaseUri(node, qec.NodeProvider);
        return ValueTask.FromResult<object?>(uri != null ? new XsAnyUri(uri) : null);
    }
}
