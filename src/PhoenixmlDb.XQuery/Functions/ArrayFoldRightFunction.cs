using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:fold-right($array as array(*), $zero as item()*, $f as function(item()*, item()*) as item()*) as item()*
/// </summary>
public sealed class ArrayFoldRightFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "fold-right");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var accumulator = arguments[1];
        var f = arguments[2] as XQueryFunction;

        if (f == null)
            return accumulator;

        for (var i = array.Count - 1; i >= 0; i--)
        {
            accumulator = await f.InvokeAsync([array[i], accumulator], context);
        }

        return accumulator;
    }
}
