using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:string-to-codepoints($arg) as xs:integer*
/// </summary>
public sealed class StringToCodepointsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string-to-codepoints");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Atomize nodes to get their string value
        arg = DataFunction.Atomize(arg);
        if (arg is Xdm.XsUntypedAtomic ua) arg = ua.Value;
        if (arg is Xdm.XsTypedString ts) arg = ts.Value;
        if (arg is not string)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Expected xs:string argument for fn:string-to-codepoints, got {arg?.GetType().Name}");
        var str = (string)arg;
        var codepoints = str.EnumerateRunes().Select(r => (object?)(long)r.Value).ToArray();
        return ValueTask.FromResult<object?>(codepoints);
    }
}
