using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:filter($map as map(*), $pred as function(xs:anyAtomicType, item()*) as xs:boolean) as map(*)
/// Returns a map containing entries that satisfy a predicate (XPath 4.0).
/// </summary>
public sealed class MapFilterFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "filter");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var pred = arguments[1] as XQueryFunction;
        if (map == null || pred == null) return MapHelper.NewMap();

        var result = MapHelper.NewMap();
        foreach (var kvp in map)
        {
            var match = await pred.InvokeAsync([kvp.Key, kvp.Value], context).ConfigureAwait(false);
            if (match is true || (match is not false && match != null))
                result[kvp.Key] = kvp.Value;
        }
        return result;
    }
}
