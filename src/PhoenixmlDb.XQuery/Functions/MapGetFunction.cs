using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:get($map as map(*), $key as xs:anyAtomicType) as item()*
/// </summary>
public sealed class MapGetFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "get");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = MapHelper.RequireMap(arguments[0], "map:get");
        var key = MapHelper.RequireSingleAtomicKey(arguments[1], "map:get");

        if (MapKeyHelper.TryGetValue(map, key, out var value))
            return ValueTask.FromResult(value);

        return ValueTask.FromResult<object?>(null);
    }
}
