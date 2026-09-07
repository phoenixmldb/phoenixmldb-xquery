using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:put($map as map(*), $key as xs:anyAtomicType, $value as item()*) as map(*)
/// </summary>
public sealed class MapPutFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "put");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var key = QueryExecutionContext.AtomizeTyped(arguments[1]);
        var value = arguments[2];

        var result = new Execution.OrderedXdmMap(
            map ?? MapHelper.NewMap(),
            Execution.XdmMapKeyComparer.Instance);

        if (key != null)
        {
            // OrderedXdmMap's indexer does the right XPath 4.0 thing: an existing
            // key (matched via XdmMapKeyComparer, including cross-type same-key —
            // untypedAtomic/string, numeric cross-type, NaN, duration, anyURI) keeps
            // its position and updates the value; a new key is appended. The old
            // RemoveByKey+re-add dance moved an existing key to the end, which is
            // wrong for 4.0 — dropped.
            result[key] = value;
        }

        return ValueTask.FromResult<object?>(result);
    }
}
