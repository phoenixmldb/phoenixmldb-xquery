using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:generate-id() as xs:string (context item)</summary>
public sealed class GenerateId0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "generate-id");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext;
        var contextItem = ctx?.ContextItem;
        if (contextItem == null)
            throw context.Error("XPDY0002", "Context item is absent");
        if (contextItem is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:generate-id expects a node as context item, got {contextItem.GetType().Name}");
        return new GenerateIdFunction().InvokeAsync([contextItem], context);
    }
}
