using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:subarray($array as array(*), $start as xs:integer, $length as xs:integer) as array(*)
/// </summary>
public sealed class ArraySubarray3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "subarray");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "start"), Type = XdmSequenceType.Integer },
        new() { Name = new QName(NamespaceId.None, "length"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var start = ArrayHelper.RequireIntegerPosition(arguments[1], "array:subarray");
        var length = ArrayHelper.RequireIntegerPosition(arguments[2], "array:subarray");

        if (start < 1 || length < 0 || start + length - 1 > array.Count)
        {
            throw new Execution.XQueryRuntimeException("FOAY0001", $"Array position out of bounds");
        }

        var result = array.Skip(start - 1).Take(length).ToList();
        return ValueTask.FromResult<object?>(result);
    }
}
