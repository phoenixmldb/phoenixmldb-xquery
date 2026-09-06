using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:group-by($seq as item()*, $key as function(item()) as xs:anyAtomicType) as map(xs:anyAtomicType, item()*)
/// Groups sequence items by a key function, returning a map of key → items (XPath 4.0).
/// </summary>
public sealed class MapGroupByFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "group-by");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var keyFn = arguments[1] as XQueryFunction;
        if (seq == null || keyFn == null) return MapHelper.NewMap();

        var items = seq is object?[] arr ? arr : new[] { seq };
        var groups = new Dictionary<object, List<object?>>();

        foreach (var item in items)
        {
            var key = await keyFn.InvokeAsync([item], context).ConfigureAwait(false);
            var atomKey = QueryExecutionContext.AtomizeTyped(key) ?? key ?? "";

            if (!groups.TryGetValue(atomKey, out var group))
            {
                group = [];
                groups[atomKey] = group;
            }
            group.Add(item);
        }

        var result = MapHelper.NewMap();
        foreach (var (k, v) in groups)
            result[k] = v.Count == 1 ? v[0] : v.ToArray();
        return result;
    }
}
