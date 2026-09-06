using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:root() as node() (uses context item)
/// </summary>
public sealed class Root0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "root");
    public override XdmSequenceType ReturnType => XdmSequenceType.Node;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext;
        var contextItem = ctx?.ContextItem;
        if (contextItem == null)
            throw context.Error("XPDY0002", "Context item is absent");
        if (contextItem is not XdmNode node)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:root expects a node as context item, got {contextItem.GetType().Name}");
        return ValueTask.FromResult<object?>(RootFunction.TraverseToRoot(node, context));
    }
}
