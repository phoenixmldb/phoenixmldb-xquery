using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:members($array as array(*)) as array(item()*)* — each member wrapped in its own array (XPath 4.0).
/// </summary>
public sealed class ArrayMembersFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "members");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // Each member becomes a single-element array
        var result = new List<object?>();
        foreach (var member in list)
            result.Add(new List<object?> { member });
        return ValueTask.FromResult<object?>(result.ToArray());
    }
}
