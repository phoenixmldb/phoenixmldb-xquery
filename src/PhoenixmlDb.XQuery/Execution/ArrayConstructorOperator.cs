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
/// Array constructor: [a, b, c] or array { expr }
/// </summary>
public sealed class ArrayConstructorOperator : PhysicalOperator
{
    public required ArrayConstructorKind Kind { get; init; }
    public required IReadOnlyList<PhysicalOperator> Members { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var array = new List<object?>();
        if (Kind == ArrayConstructorKind.Square)
        {
            // Square: each member expression becomes a single array entry (may be a sequence)
            foreach (var member in Members)
            {
                var items = new List<object?>();
                await foreach (var item in member.ExecuteAsync(context))
                    items.Add(item);
                array.Add(items.Count == 1 ? items[0] : items.Count == 0 ? null : items.ToArray());
            }
        }
        else
        {
            // Curly: enclosed expr sequence becomes the array
            foreach (var member in Members)
            {
                await foreach (var item in member.ExecuteAsync(context))
                    array.Add(item);
            }
        }
        // Return as List<object?> — NOT object?[] — so that VariableOperator
        // recognises this as an XDM array (single item) rather than an XDM
        // sequence (which would be decomposed into individual items).
        yield return array;
    }
}
