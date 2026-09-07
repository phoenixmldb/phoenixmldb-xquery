using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:entries($map as map(*)) as map(xs:string, item()*)* — returns sequence of entry maps (XPath 4.0).
/// Each entry is a map with "key" and "value" entries.
/// </summary>
public sealed class MapEntriesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "entries");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        if (map == null) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var entries = new List<object?>();
        foreach (var kvp in map)
        {
            var entry = new Execution.OrderedXdmMap(Execution.XdmMapKeyComparer.Instance)
            {
                ["key"] = kvp.Key,
                ["value"] = kvp.Value
            };
            entries.Add(entry);
        }
        return ValueTask.FromResult<object?>(entries.ToArray());
    }
}
