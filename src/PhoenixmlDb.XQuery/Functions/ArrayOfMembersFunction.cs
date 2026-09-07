using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:of-members($seq as array(*)*) as array(*) — combines arrays into one (XPath 4.0).
/// </summary>
public sealed class ArrayOfMembersFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "of-members");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(new List<object?>());

        var items = seq is object?[] arr ? arr : new[] { seq };
        var result = new List<object?>();
        foreach (var item in items)
        {
            if (item is List<object?> memberArray)
                result.AddRange(memberArray);
            else if (item != null)
                result.Add(item);
        }
        return ValueTask.FromResult<object?>(result);
    }
}
