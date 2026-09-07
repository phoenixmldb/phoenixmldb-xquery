using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:tail($arg) as item()*
/// </summary>
public sealed class TailFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "tail");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // XDM arrays (List<object?>) are single items — the tail of a one-item
        // sequence containing an array is the empty sequence.
        if (arg is List<object?>)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        if (arg is IEnumerable<object?> seq)
            return ValueTask.FromResult<object?>(seq.Skip(1));

        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}
