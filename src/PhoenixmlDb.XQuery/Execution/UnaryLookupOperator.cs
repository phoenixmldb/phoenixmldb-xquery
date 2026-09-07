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
/// Unary lookup operator (?key) — looks up a key on the context item.
/// Per XQuery 3.1 spec: key can be a sequence; each key is looked up.
/// Context item must be a map, array, or function — otherwise XPTY0004.
/// </summary>
public sealed class UnaryLookupOperator : PhysicalOperator
{
    public PhysicalOperator? Key { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var contextItem = context.ContextItem;
        if (contextItem == null)
            yield break;

        if (Key == null)
        {
            // Wildcard lookup ?* — return all values from context item
            await foreach (var v in LookupHelper.WildcardLookup(contextItem))
                yield return v;
            yield break;
        }

        // Collect all keys — key can be a sequence
        var keys = new List<object?>();
        await foreach (var item in Key.ExecuteAsync(context))
            keys.Add(item);

        // Empty key sequence → empty result
        if (keys.Count == 0)
            yield break;

        foreach (var rawKey in keys)
        {
            await foreach (var v in LookupHelper.LookupByKey(contextItem, rawKey))
                yield return v;
        }
    }
}
