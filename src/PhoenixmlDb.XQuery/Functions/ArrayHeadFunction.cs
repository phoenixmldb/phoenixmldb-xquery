using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:head($array as array(*)) as item()*
/// </summary>
public sealed class ArrayHeadFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "head");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;

        if (array == null || array.Count == 0)
        {
            throw new InvalidOperationException("array:head called on empty array");
        }

        return ValueTask.FromResult(array[0]);
    }
}
