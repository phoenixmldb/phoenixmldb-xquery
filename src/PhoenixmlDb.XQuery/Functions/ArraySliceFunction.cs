using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:slice($array as array(*), $start as xs:integer?, $end as xs:integer?,
///             $step as xs:integer?) as array(*)
/// Flexible sub-array with start/end/step (XPath 4.0).
/// </summary>
public sealed class ArraySliceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "slice");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "start"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "end"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "step"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MinArity => 1;
    public override int MaxArity => 4;

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list) return ValueTask.FromResult<object?>(new List<object?>());
        var len = list.Count;

        var start = arguments.Count > 1 && arguments[1] != null ? Convert.ToInt32(arguments[1]) : 1;
        var end = arguments.Count > 2 && arguments[2] != null ? Convert.ToInt32(arguments[2]) : len;
        var step = arguments.Count > 3 && arguments[3] != null ? Convert.ToInt32(arguments[3]) : 1;

        if (start < 0) start = len + start + 1;
        if (end < 0) end = len + end + 1;
        if (step == 0) return ValueTask.FromResult<object?>(new List<object?>());

        var result = new List<object?>();
        if (step > 0)
        {
            for (var i = Math.Max(1, start); i <= Math.Min(len, end); i += step)
                result.Add(list[i - 1]);
        }
        else
        {
            for (var i = Math.Min(len, start); i >= Math.Max(1, end); i += step)
                result.Add(list[i - 1]);
        }
        return ValueTask.FromResult<object?>(result);
    }
}
