using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:path() — 0-arg version uses context item.
/// </summary>
public sealed class Path0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "path");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var node = context.ContextItem as XdmNode;
        if (node is null)
            return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(PathFunction.ComputePath(node, context.NodeStore));
    }
}

// ─── fn:id ─────────────────────────────────────────────────────────────────
