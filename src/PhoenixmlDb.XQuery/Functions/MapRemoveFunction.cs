using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:remove($map as map(*), $keys as xs:anyAtomicType*) as map(*)
/// </summary>
public sealed class MapRemoveFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "remove");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "keys"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var rawKeys = arguments[1] as IEnumerable<object?> ?? (arguments[1] != null ? [arguments[1]] : []);

        var result = MapHelper.CopyMap(map ?? MapHelper.NewMap());

        foreach (var rawKey in rawKeys)
        {
            var key = QueryExecutionContext.AtomizeTyped(rawKey);
            if (key != null)
            {
                MapKeyHelper.RemoveByKey(result, key);
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}
