using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:generate-id($arg as node()?) as xs:string</summary>
public sealed class GenerateIdFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "generate-id");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var node = arguments[0];
        // Normalize empty sequence (object[]{}) to null per fn:generate-id($arg as node()?) signature
        if (node is object?[] { Length: 0 }) node = null;
        if (node == null) return ValueTask.FromResult<object?>("");
        // XPTY0004: argument must be a node
        if (node is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:generate-id expects a node, got {node.GetType().Name}");
        // Generate a stable ID based on the node's hash code
        var hash = node.GetHashCode();
        return ValueTask.FromResult<object?>($"N{(hash & 0x7FFFFFFF):X8}");
    }
}
