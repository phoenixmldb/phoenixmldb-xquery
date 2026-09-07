using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:put($array as array(*), $position as xs:integer, $member as item()*) as array(*)
/// </summary>
public sealed class ArrayPutFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "put");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "position"), Type = XdmSequenceType.Integer },
        new() { Name = new QName(NamespaceId.None, "member"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;
        var position = ArrayHelper.RequireIntegerPosition(arguments[1], "array:put");
        var member = arguments[2];

        if (array == null || position < 1 || position > array.Count)
        {
            throw new Execution.XQueryRuntimeException("FOAY0001", $"Array position out of bounds");
        }

        var result = new List<object?>(array);
        result[position - 1] = member;

        return ValueTask.FromResult<object?>(result);
    }
}
