using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:foot($array as array(*)) as item()* — returns last member (XPath 4.0).
/// </summary>
public sealed class ArrayFootFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "foot");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is List<object?> list && list.Count > 0)
            return ValueTask.FromResult(list[^1]);
        return ValueTask.FromResult<object?>(null);
    }
}
