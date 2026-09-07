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
