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

        // F&O 3.1 §17.3.10: FOAY0001 for a start outside 1..size+1, FOAY0002 for a negative
        // length, then FOAY0001 for an end past the array. In long: a start clamped to
        // int.MaxValue plus a length overflowed int, passed the check and gave [] (QT3
        // array-subarray-317); a negative length was FOAY0001 (array-subarray-311).
        if (start < 1 || start > (long)array.Count + 1)
            throw new Execution.XQueryRuntimeException("FOAY0001", $"Array position out of bounds");
        if (length < 0)
            throw new Execution.XQueryRuntimeException("FOAY0002", $"Negative array length: {length}");
        if ((long)start + length - 1 > array.Count)
            throw new Execution.XQueryRuntimeException("FOAY0001", $"Array position out of bounds");

        var result = array.Skip(start - 1).Take(length).ToList();
        return ValueTask.FromResult<object?>(result);
    }
}
