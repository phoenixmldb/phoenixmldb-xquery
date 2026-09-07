using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:root($arg) as node()?
/// </summary>
public sealed class RootFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "root");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalNode;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        // Normalize empty sequence (object[]{}) to null per fn:root($arg as node()?) signature
        if (arg is object?[] { Length: 0 }) arg = null;
        if (arg == null)
            return ValueTask.FromResult<object?>(null);
        if (arg is not XdmNode node)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:root expects a node, got {arg.GetType().Name}");

        return ValueTask.FromResult<object?>(TraverseToRoot(node, context));
    }

    internal static XdmNode TraverseToRoot(XdmNode node, Ast.ExecutionContext context)
    {
        var current = node;
        if (context is PhoenixmlDb.XQuery.Execution.QueryExecutionContext qec && qec.NodeProvider != null)
        {
            while (current.Parent is { } parentId && parentId != NodeId.None)
            {
                var parent = qec.NodeProvider.GetNode(parentId);
                if (parent == null) break;
                current = parent;
            }
        }
        return current;
    }
}
