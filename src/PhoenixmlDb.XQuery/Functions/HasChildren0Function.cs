using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:has-children() as xs:boolean (context item)</summary>
public sealed class HasChildren0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "has-children");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext ?? throw context.Error("XPDY0002", "Context item absent");
        var item = ctx.ContextItem;
        if (item == null) throw context.Error("XPDY0002", "Context item is absent");
        if (item is not XdmNode)
            throw context.Error("XPTY0004", "Context item for fn:has-children() is not a node");
        return ValueTask.FromResult<object?>(HasChildrenFunction.HasChildren(item, context));
    }
}
