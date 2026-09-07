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
/// Where clause operator.
/// </summary>
public sealed class WhereClauseOperator : FlworClauseOperator
{
    public required PhysicalOperator ConditionOperator { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        // EBV rules: if 2+ items and the first is not a node → FORG0006.
        // If first is a node → EBV is true (shortcut).
        object? first = null;
        bool hasFirst = false;
        await foreach (var item in ConditionOperator.ExecuteAsync(context))
        {
            if (!hasFirst)
            {
                first = item;
                hasFirst = true;
                // If first item is a node, EBV is true regardless of remaining items
                if (first is Xdm.Nodes.XdmNode or Xdm.TextNodeItem
                    or System.Xml.XmlNode or System.Xml.Linq.XNode)
                    break;
            }
            else
            {
                // 2+ items, first is not a node → FORG0006
                throw new XQueryRuntimeException("FORG0006",
                    "Effective boolean value not defined for a sequence of two or more items starting with a non-node value");
            }
        }

        if (QueryExecutionContext.EffectiveBooleanValue(first))
        {
            yield return new Dictionary<QName, object?>();
        }
    }
}
