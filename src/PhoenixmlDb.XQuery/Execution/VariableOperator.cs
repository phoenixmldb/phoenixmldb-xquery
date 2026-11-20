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
/// Returns a variable value.
/// </summary>
public sealed class VariableOperator : PhysicalOperator
{
    public required QName VariableName { get; init; }

    internal override bool SupportsSync => true;

    internal override object? EvaluateSync(QueryExecutionContext context)
    {
        var value = context.GetVariable(VariableName);
        switch (value)
        {
            case object?[] arr:
                // The variable's own array, not a copy: no consumer of a sync result writes into an
                // array it did not create (each one reads it, or copies it into a new list or
                // array first). A copy here made every read of a large sequence variable cost
                // O(n), so `$ids[1]` or `head($ids)` once per node was quadratic.
                return arr.Length switch { 0 => null, 1 => arr[0], _ => arr };
            case List<object?> or IDictionary<object, object?>:
                return value; // an XDM array or map is ONE item
            case IEnumerable<object?> seq:
                var items = new List<object?>();
                foreach (var item in seq)
                    items.Add(item);
                return SyncResultOf(items);
            default:
                return value;
        }
    }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        var value = context.GetVariable(VariableName);
        // Explicitly handle sequences (object?[]) by enumerating items
        if (value is object?[] arr)
        {
            foreach (var item in arr)
                yield return item;
        }
        // XDM arrays (List<object?>) and maps (Dictionary) are single items — yield as-is
        else if (value is List<object?> or IDictionary<object, object?>)
        {
            yield return value;
        }
        else if (value is IEnumerable<object?> seq)
        {
            foreach (var item in seq)
                yield return item;
        }
        else if (value != null)
        {
            yield return value;
        }
    }
}
