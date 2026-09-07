using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:trunk($array as array(*)) as array(*) — all members except last (XPath 4.0).
/// </summary>
public sealed class ArrayTrunkFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "trunk");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is List<object?> list && list.Count > 1)
            return ValueTask.FromResult<object?>(new List<object?>(list.GetRange(0, list.Count - 1)));
        return ValueTask.FromResult<object?>(new List<object?>());
    }
}
