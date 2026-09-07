using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:head($arg) as item()?
/// </summary>
public sealed class HeadFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "head");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(null);

        // XDM arrays (List<object?>) are single items — the head of a one-item
        // sequence containing an array is the array itself, not its first member.
        if (arg is List<object?>)
            return ValueTask.FromResult<object?>(arg);

        if (arg is IEnumerable<object?> seq)
            return ValueTask.FromResult<object?>(seq.FirstOrDefault());

        return ValueTask.FromResult<object?>(arg);
    }
}
