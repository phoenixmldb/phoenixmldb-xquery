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
/// Thin arrow operator (->) for XPath 4.0.
/// Evaluates the expression, pushes its result as the context item,
/// then evaluates the function call in that context.
/// </summary>
public sealed class ThinArrowOperator : PhysicalOperator
{
    public required PhysicalOperator Expression { get; init; }
    public required PhysicalOperator FunctionCall { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? exprResult = null;
        await foreach (var item in Expression.ExecuteAsync(context))
            exprResult = item;

        // Push expression result as context item
        context.PushContextItem(exprResult, 1, 1);
        try
        {
            await foreach (var result in FunctionCall.ExecuteAsync(context))
                yield return result;
        }
        finally
        {
            context.PopContextItem();
        }
    }
}
