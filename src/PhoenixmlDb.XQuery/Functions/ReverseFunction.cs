using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:reverse($arg) as item()*
/// </summary>
public sealed class ReverseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "reverse");
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

        // XDM arrays (List<object?>) are single items — reversing a one-item
        // sequence containing an array yields the same array.
        if (arg is List<object?>)
            return ValueTask.FromResult<object?>(new[] { arg });

        if (arg is IEnumerable<object?> seq)
            return ValueTask.FromResult<object?>(seq.Reverse().ToArray());

        return ValueTask.FromResult<object?>(new[] { arg });
    }
}
