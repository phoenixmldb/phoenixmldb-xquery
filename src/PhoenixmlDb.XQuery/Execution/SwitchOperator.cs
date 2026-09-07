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
/// Switch expression.
/// </summary>
public sealed class SwitchOperator : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required IReadOnlyList<SwitchCaseOperator> Cases { get; init; }
    public required PhysicalOperator Default { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? operandVal = null;
        int operandCount = 0;
        await foreach (var item in Operand.ExecuteAsync(context))
        {
            operandVal = item;
            operandCount++;
            if (operandCount > 1)
                throw new XQueryRuntimeException("XPTY0004",
                    "Switch operand must be a single atomic value, got a sequence");
        }
        // Atomize the operand
        operandVal = QueryExecutionContext.Atomize(operandVal);

        foreach (var @case in Cases)
        {
            foreach (var valueOp in @case.Values)
            {
                object? caseVal = null;
                int caseCount = 0;
                await foreach (var item in valueOp.ExecuteAsync(context))
                {
                    caseVal = item;
                    caseCount++;
                    if (caseCount > 1)
                        throw new XQueryRuntimeException("XPTY0004",
                            "Switch case operand must be a single atomic value, got a sequence");
                }
                // Atomize the case value
                caseVal = QueryExecutionContext.Atomize(caseVal);

                // Per XQuery 3.1 §3.14: switch comparison uses eq semantics
                // except NaN matches NaN and () matches ()
                bool matches = false;
                if (operandVal == null && caseVal == null)
                    matches = true;
                else if (operandVal is double dOp && double.IsNaN(dOp)
                         && caseVal is double dCase && double.IsNaN(dCase))
                    matches = true;
                else if (operandVal is float fOp && float.IsNaN(fOp)
                         && caseVal is float fCase && float.IsNaN(fCase))
                    matches = true;
                else if (operandVal is double dOp2 && double.IsNaN(dOp2)
                         && caseVal is float fCase2 && float.IsNaN(fCase2))
                    matches = true;
                else if (operandVal is float fOp2 && float.IsNaN(fOp2)
                         && caseVal is double dCase2 && double.IsNaN(dCase2))
                    matches = true;
                else if (operandVal != null && caseVal != null)
                    matches = TypeCastHelper.DeepEquals(operandVal, caseVal, nodeProvider: context.NodeProvider);

                if (matches)
                {
                    await foreach (var result in @case.Result.ExecuteAsync(context))
                        yield return result;
                    yield break;
                }
            }
        }

        // No case matched, use default
        await foreach (var result in Default.ExecuteAsync(context))
            yield return result;
    }
}
