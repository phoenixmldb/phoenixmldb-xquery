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
/// Lookup expression: base?key or base?*
/// Per XQuery 3.1 spec: distributes lookup over each item in the base sequence.
/// Key can be a sequence — each key is looked up on each item.
/// </summary>
public sealed class LookupOperator : PhysicalOperator
{
    public required PhysicalOperator Base { get; init; }
    public PhysicalOperator? Key { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Collect all base items — postfix lookup distributes over the sequence
        var baseItems = new List<object?>();
        await foreach (var item in Base.ExecuteAsync(context))
            baseItems.Add(item);

        if (baseItems.Count == 0)
            yield break;

        if (Key == null)
        {
            // Wildcard lookup ?* — return all values from each item
            foreach (var baseVal in baseItems)
            {
                await foreach (var v in LookupHelper.WildcardLookup(baseVal))
                    yield return v;
            }
            yield break;
        }

        // Collect all keys — key can be a sequence like ?(1 to 3)
        var keys = new List<object?>();
        await foreach (var item in Key.ExecuteAsync(context))
            keys.Add(item);

        // Empty key sequence → empty result (not an error)
        if (keys.Count == 0)
            yield break;

        foreach (var baseVal in baseItems)
        {
            foreach (var rawKey in keys)
            {
                await foreach (var v in LookupHelper.LookupByKey(baseVal, rawKey))
                    yield return v;
            }
        }
    }
}
