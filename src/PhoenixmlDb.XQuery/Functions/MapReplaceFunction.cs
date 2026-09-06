using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:replace($map as map(*), $key as xs:anyAtomicType, $value as item()*) as map(*)
/// Replaces a value in a map (like map:put but only if key exists) (XPath 4.0).
/// </summary>
public sealed class MapReplaceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "replace");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var key = QueryExecutionContext.AtomizeTyped(arguments[1]) ?? arguments[1]!;
        var value = arguments[2];

        var result = MapHelper.CopyMap(map ?? MapHelper.NewMap());
        if (result.ContainsKey(key))
            result[key] = value;
        return ValueTask.FromResult<object?>(result);
    }
}
