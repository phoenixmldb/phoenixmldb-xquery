using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

public sealed class CollationKeyFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collation-key");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        // Per spec: $value must be xs:string (xs:untypedAtomic and xs:anyURI promote to string, other types are XPTY0004)
        if (arg is Xdm.XsUntypedAtomic ua)
            arg = ua.Value;
        if (arg is Xdm.XsAnyUri anyUri)
            arg = anyUri.Value;
        if (arg is not string)
            throw context.Error("XPTY0004",
                $"First argument to fn:collation-key must be xs:string, got {arg?.GetType().Name ?? "empty sequence"}");
        var collUri = arguments[1]?.ToString();
        return ValueTask.FromResult<object?>(
            CollationKey1Function.ComputeCollationKey((string)arg, collUri));
    }
}
