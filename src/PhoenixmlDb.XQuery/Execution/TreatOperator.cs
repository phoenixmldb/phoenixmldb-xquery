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
/// Treat expression: runtime type assertion that raises XPDY0050 on mismatch.
/// Unlike cast, treat does not convert values — it only checks that they already match.
/// </summary>
public sealed class TreatOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required XdmSequenceType TargetType { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var items = new List<object?>();
        await foreach (var item in Operand.ExecuteAsync(context))
            items.Add(item);

        // Check cardinality
        var count = items.Count;
        switch (TargetType.Occurrence)
        {
            case Occurrence.ExactlyOne when count != 1:
                throw new XQueryRuntimeException("XPDY0050",
                    $"Required cardinality of value treated as {TargetType} is exactly one, but the sequence has {count} item(s)");
            case Occurrence.ZeroOrOne when count > 1:
                throw new XQueryRuntimeException("XPDY0050",
                    $"Required cardinality of value treated as {TargetType} is zero or one, but the sequence has {count} items");
            case Occurrence.OneOrMore when count < 1:
                throw new XQueryRuntimeException("XPDY0050",
                    $"Required cardinality of value treated as {TargetType} is one or more, but the sequence is empty");
        }

        // Check item types (unless target is item())
        if (TargetType.ItemType != ItemType.Item)
        {
            foreach (var item in items)
            {
                if (item != null && !TypeCastHelper.MatchesSequenceItemType(item, TargetType, context.SchemaProvider))
                    throw new XQueryRuntimeException("XPDY0050",
                        $"An item in the sequence does not match the required type {TargetType}: got {XdmShape.TypeNameOf(item)}");

                // Check derived integer subtype range (e.g., treat as xs:negativeInteger)
                if (item != null && TargetType.DerivedIntegerType != null && TargetType.ItemType == ItemType.Integer)
                {
                    if (!TypeCastHelper.MatchesDerivedIntegerRange(item, TargetType.DerivedIntegerType))
                        throw new XQueryRuntimeException("XPDY0050",
                            $"An item in the sequence does not match the required type {TargetType}: value {item} is out of range for xs:{TargetType.DerivedIntegerType}");
                }
            }
        }

        foreach (var item in items)
            yield return item;
    }
}
