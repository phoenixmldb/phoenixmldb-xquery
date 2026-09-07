using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:append($array as array(*), $appendage as item()*) as array(*)
/// </summary>
public sealed class ArrayAppendFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "append");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "appendage"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var appendage = arguments[1];

        var result = new List<object?>(array) { appendage };
        return ValueTask.FromResult<object?>(result);
    }
}
