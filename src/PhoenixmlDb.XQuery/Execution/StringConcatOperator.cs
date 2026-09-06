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
/// String concatenation: a || b || c
/// </summary>
public sealed class StringConcatOperator : PhysicalOperator
{
    public required IReadOnlyList<PhysicalOperator> Operands { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var parts = new List<string>();
        foreach (var op in Operands)
        {
            object? val = null;
            await foreach (var item in op.ExecuteAsync(context))
            { val = item; break; }
            parts.Add(Functions.ConcatFunction.XQueryStringValue(val));
        }
        yield return string.Concat(parts);
    }
}
