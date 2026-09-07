using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:join($arrays as array(*)*) as array(*)
/// </summary>
public sealed class ArrayJoinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "join");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arrays"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ZeroOrMore } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var result = new List<object?>();
        var arg = arguments[0];

        // Single array argument: return it as-is (join of one array)
        if (arg is List<object?> singleArray)
        {
            return ValueTask.FromResult<object?>(new List<object?>(singleArray));
        }

        // Sequence of arrays: join all members
        if (arg is IEnumerable<object?> arrays)
        {
            foreach (var array in arrays)
            {
                if (array is IList<object?> list)
                {
                    result.AddRange(list);
                }
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}
