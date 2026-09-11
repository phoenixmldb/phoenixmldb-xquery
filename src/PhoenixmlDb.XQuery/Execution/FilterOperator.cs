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
/// Filters items by predicate.
/// </summary>
public sealed class FilterOperator : PhysicalOperator
{
    public required PhysicalOperator Input { get; init; }
    public required PhysicalOperator PredicateOperator { get; init; }

    /// <summary>
    /// Whether this filter uses positional predicates (position()/last()).
    /// When false, items can be streamed without full materialization.
    /// </summary>
    public bool RequiresPositionalAccess { get; init; } = true;

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        if (RequiresPositionalAccess)
        {
            // Must materialize for position/last functions
            var items = new List<object?>();
            await foreach (var item in Input.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                items.Add(item);
                context.CheckMaterializationLimit(items.Count);
            }

            var position = 0;
            foreach (var item in items)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                position++;
                context.PushContextItem(item, position, items.Count);
                try
                {
                    var result = await EvaluatePredicateAsync(context);

                    // Numeric values select by position, anything else by EBV — see PositionalPredicate.
                    if (PositionalPredicate.Selects(result, position))
                        yield return item;
                }
                finally
                {
                    context.PopContextItem();
                }
            }
        }
        else
        {
            // Stream items without materialization — position/last not needed
            var position = 0;
            await foreach (var item in Input.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                position++;
                context.PushContextItem(item, position, -1);
                try
                {
                    var result = await EvaluatePredicateAsync(context);
                    // Numeric values select by position, anything else by EBV — see PositionalPredicate.
                    if (PositionalPredicate.Selects(result, position))
                        yield return item;
                }
                finally
                {
                    context.PopContextItem();
                }
            }
        }
    }

    private async ValueTask<object?> EvaluatePredicateAsync(QueryExecutionContext context)
    {
        // Execute the predicate operator and collect up to 2 items to detect multi-item sequences
        object? first = null;
        int count = 0;
        await foreach (var item in PredicateOperator.ExecuteAsync(context))
        {
            if (count == 0)
                first = item;
            count++;
            if (count > 1)
            {
                // Multiple items: if first is a node, result is the sequence (treated as true by EBV).
                // If first is not a node, FORG0006 per EBV rules for sequences of 2+ items.
                if (first is not Xdm.Nodes.XdmNode && first is not Xdm.TextNodeItem
                    && first is not System.Xml.XmlNode && first is not System.Xml.Linq.XNode)
                {
                    throw new XQueryRuntimeException("FORG0006",
                        "Effective boolean value not defined for a sequence of two or more items starting with a non-node value");
                }
                return true;
            }
        }
        return first;
    }
}
