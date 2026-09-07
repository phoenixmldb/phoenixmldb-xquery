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
/// Inline function expression: function($x) { body }
/// </summary>
public sealed class InlineFunctionOperator : PhysicalOperator
{
    public required IReadOnlyList<FunctionParameter> Parameters { get; init; }
    public required XQueryExpression Body { get; init; }
    public XdmSequenceType? DeclaredReturnType { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        // Create a closure that captures the current scope
        yield return new InlineFunctionItem(Parameters, Body, context, DeclaredReturnType);
    }
}
