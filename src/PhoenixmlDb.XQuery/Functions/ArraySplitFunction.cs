using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:split($array as array(*), $sizes as xs:integer*) as array(array(*))
/// Splits an array into chunks (XPath 4.0).
/// </summary>
public sealed class ArraySplitFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "split");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "size"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list) return ValueTask.FromResult<object?>(new List<object?>());
        var chunkSize = Convert.ToInt32(arguments[1]);
        if (chunkSize <= 0) return ValueTask.FromResult<object?>(new List<object?>());

        var result = new List<object?>();
        for (var i = 0; i < list.Count; i += chunkSize)
        {
            var chunk = new List<object?>(list.GetRange(i, Math.Min(chunkSize, list.Count - i)));
            result.Add(chunk);
        }
        return ValueTask.FromResult<object?>(result);
    }
}
