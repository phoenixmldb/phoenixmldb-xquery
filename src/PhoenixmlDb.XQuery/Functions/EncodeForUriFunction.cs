using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:encode-for-uri($arg) as xs:string
/// </summary>
public sealed class EncodeForUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "encode-for-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>("");
        var s = StringArgument.AsString(arg)
            ?? throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Expected xs:string argument for fn:encode-for-uri, got {arg.GetType().Name}");
        return ValueTask.FromResult<object?>(Uri.EscapeDataString(s));
    }
}
