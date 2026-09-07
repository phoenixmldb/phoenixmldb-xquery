using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Map constructor: map { key: value, ... }
/// </summary>
public sealed class MapConstructorOperator : PhysicalOperator
{
    public required IReadOnlyList<MapEntryOperator> Entries { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var map = new OrderedXdmMap(XdmMapKeyComparer.Instance);
        foreach (var entry in Entries)
        {
            // Collect all key items to check for singleton.
            // Per XQuery 3.1 §3.11.1: the key expression is atomized.
            var keyItems = new List<object?>();
            await foreach (var item in entry.Key.ExecuteAsync(context))
            {
                var atomized = context.AtomizeWithNodes(item);
                if (atomized is object?[] atomizedSeq)
                {
                    foreach (var a in atomizedSeq)
                        keyItems.Add(a);
                }
                else
                    keyItems.Add(atomized);
            }
            if (keyItems.Count == 0)
                throw new XQueryRuntimeException("XPTY0004",
                    "Map key must be a single atomic value, got an empty sequence");
            if (keyItems.Count > 1)
                throw new XQueryRuntimeException("XPTY0004",
                    "Map key must be a single atomic value, got a sequence of " + keyItems.Count + " items");
            var key = keyItems[0];
            // Collect all value items — map values are sequences per XPath spec
            var valueItems = new List<object?>();
            await foreach (var item in entry.Value.ExecuteAsync(context))
                valueItems.Add(item);
            var value = valueItems.Count == 1 ? valueItems[0] : valueItems.Count == 0 ? null : valueItems.ToArray();
            if (key != null)
            {
                if (map.ContainsKey(key))
                    throw new XQueryRuntimeException("XQDY0137",
                        $"Duplicate key '{key}' in map constructor");
                map[key] = value;
            }
        }
        yield return map;
    }
}
