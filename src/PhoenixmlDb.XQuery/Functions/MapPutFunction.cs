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

        // Copy first: the copy shares the source's structure (a large flat source is converted to
        // the trie as it is copied), so the lookup below is cheap.
        var result = new Execution.OrderedXdmMap(
            map ?? MapHelper.NewMap(),
            Execution.XdmMapKeyComparer.Instance);

        if (key != null)
        {
            // An equal key already present is REPLACED, key included: map:put's result holds $key,
            // not the old key. The indexer keeps the stored key and swaps only the value, so
            // map:put(map{3:"x"}, xs:float('3.0'), "y") kept the xs:integer key and was not a
            // map(xs:float, xs:string) (QT3 map-put-011, xquery#83).
            //
            // Only when the stored key really differs from $key — a different type — is the map
            // rebuilt with the entry substituted in place (position kept, as XPath 4.0 requires).
            // Rebuilding on EVERY existing key made a put-per-key loop quadratic: QT3 same-key-023
            // went from 3.8s to past its 120s limit.
            if (result.TryGetStoredKey(key, out var storedKey)
                && (storedKey.GetType() != key.GetType() || !storedKey.Equals(key)))
            {
                var comparer = Execution.XdmMapKeyComparer.Instance;
                var replaced = new Execution.OrderedXdmMap(comparer);
                foreach (var entry in result)
                    replaced.Add(comparer.Equals(entry.Key, key) ? key : entry.Key,
                                 comparer.Equals(entry.Key, key) ? value : entry.Value);
                return ValueTask.FromResult<object?>(replaced);
            }

            result[key] = value;
        }

        return ValueTask.FromResult<object?>(result);
    }
}
