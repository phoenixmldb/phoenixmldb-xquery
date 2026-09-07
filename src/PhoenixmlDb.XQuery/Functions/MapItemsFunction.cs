using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:items($map as map(*)) as array(union(xs:anyAtomicType, item()*))*
/// Returns a sequence of [key, value] arrays for each entry (XPath 4.0).
/// </summary>
public sealed class MapItemsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "items");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        if (map == null) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var items = new List<object?>();
        foreach (var kvp in map)
            items.Add(new List<object?> { kvp.Key, kvp.Value });
        return ValueTask.FromResult<object?>(items.ToArray());
    }
}
