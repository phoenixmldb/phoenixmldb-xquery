using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:remove($array as array(*), $positions as xs:integer*) as array(*)
/// </summary>
public sealed class ArrayRemoveFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "remove");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "positions"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var positions = arguments[1] is IEnumerable<object?> seq
            ? seq.Select(p => Convert.ToInt32(p)).ToHashSet()
            : arguments[1] != null ? [Convert.ToInt32(arguments[1])] : new HashSet<int>();

        // Validate positions are in range (1-based)
        foreach (var pos in positions)
        {
            if (pos < 1 || pos > array.Count)
                throw new XQueryRuntimeException("FOAY0001", $"Array position {pos} out of range (array size: {array.Count})");
        }

        var result = new List<object?>();
        for (var i = 0; i < array.Count; i++)
        {
            if (!positions.Contains(i + 1)) // 1-based
            {
                result.Add(array[i]);
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}
