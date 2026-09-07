using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:tail($array as array(*)) as array(*)
/// </summary>
public sealed class ArrayTailFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "tail");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;

        if (array == null || array.Count == 0)
        {
            throw new InvalidOperationException("array:tail called on empty array");
        }

        var result = array.Skip(1).ToList();
        return ValueTask.FromResult<object?>(result);
    }
}
