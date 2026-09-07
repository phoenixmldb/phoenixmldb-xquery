using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:nilled($arg as node()?) as xs:boolean?</summary>
public sealed class NilledFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "nilled");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var node = arguments[0];
        if (node == null) return ValueTask.FromResult<object?>(null);
        // XPTY0004: argument must be a node
        if (node is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:nilled expects a node, got {node.GetType().Name}");
        // Non-schema-aware: nilled is always false for elements, absent for other node types
        if (node is XdmElement) return ValueTask.FromResult<object?>(false);
        return ValueTask.FromResult<object?>(null);
    }
}
