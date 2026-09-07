using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:items($array as array(*)) as item()* — returns all members as a flat sequence (XPath 4.0).
/// </summary>
public sealed class ArrayItemsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "items");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is List<object?> list)
            return ValueTask.FromResult<object?>(list.Count == 1 ? list[0] : list.ToArray());
        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}
