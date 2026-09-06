using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:keys-where($map as map(*), $pred as function(xs:anyAtomicType) as xs:boolean) as xs:anyAtomicType*
/// Returns keys whose values satisfy a predicate (XPath 4.0).
/// </summary>
public sealed class MapKeysWhereFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "keys-where");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
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
        if (map == null || pred == null) return Array.Empty<object>();

        var result = new List<object?>();
        foreach (var kvp in map)
        {
            var match = await pred.InvokeAsync([kvp.Value], context).ConfigureAwait(false);
            if (match is true || (match is not false && match != null))
                result.Add(kvp.Key);
        }
        return result.Count == 1 ? result[0] : result.ToArray();
    }
}
