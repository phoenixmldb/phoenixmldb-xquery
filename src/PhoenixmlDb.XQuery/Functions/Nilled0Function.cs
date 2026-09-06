using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:nilled() as xs:boolean? (context item)</summary>
public sealed class Nilled0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "nilled");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext;
        var contextItem = ctx?.ContextItem;
        if (contextItem == null)
            throw context.Error("XPDY0002", "Context item is absent");
        // XPTY0004: context item must be a node
        if (contextItem is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:nilled expects a node as context item, got {contextItem.GetType().Name}");
        return new NilledFunction().InvokeAsync([contextItem], context);
    }
}
