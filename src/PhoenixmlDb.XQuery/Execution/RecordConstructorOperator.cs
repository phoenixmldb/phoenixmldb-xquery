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
/// XPath 4.0: record { name: value, ... } constructor.
/// Evaluates to a map (Dictionary) with string keys.
/// </summary>
public sealed class RecordConstructorOperator : PhysicalOperator
{
    public required IReadOnlyList<(string Name, PhysicalOperator Value)> Fields { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Records are maps with ordered fields (XPath 4.0) — preserve field order.
        var result = new OrderedXdmMap(XdmMapKeyComparer.Instance);
        foreach (var (name, valueOp) in Fields)
        {
            object? value = null;
            await foreach (var item in valueOp.ExecuteAsync(context))
                value = item;
            result[name] = value;
        }
        yield return result;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// XQuery Full-Text — Physical Operator
// ═══════════════════════════════════════════════════════════════════════════
