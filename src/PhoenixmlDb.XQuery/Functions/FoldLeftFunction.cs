using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:fold-left($seq, $zero, $f) as item()*
/// </summary>
public sealed class FoldLeftFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "fold-left");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = SequenceHelper.Flatten(arguments[0]);
        object? accumulator = arguments[1];
        var func = arguments[2] as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004", "Third argument to fn:fold-left must be a function");

        foreach (var item in seq)
            accumulator = await func.InvokeAsync([accumulator, item], context);

        return accumulator;
    }
}
