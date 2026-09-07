using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:get($array as array(*), $position as xs:integer) as item()*
/// </summary>
public sealed class ArrayGetFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "get");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "position"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;
        var position = ArrayHelper.RequireIntegerPosition(arguments[1], "array:get");

        if (array == null || position < 1 || position > array.Count)
        {
            throw new Execution.XQueryRuntimeException("FOAY0001",
                $"Array index {position} out of bounds (array size: {array?.Count ?? 0})");
        }

        return ValueTask.FromResult(array[position - 1]); // 1-based indexing
    }
}
