using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:contains($map as map(*), $key as xs:anyAtomicType) as xs:boolean
/// </summary>
public sealed class MapContainsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "contains");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = MapHelper.RequireMap(arguments[0], "map:contains");
        var key = MapHelper.RequireSingleAtomicKey(arguments[1], "map:contains");
        return ValueTask.FromResult<object?>(MapKeyHelper.ContainsKey(map, key));
    }
}
