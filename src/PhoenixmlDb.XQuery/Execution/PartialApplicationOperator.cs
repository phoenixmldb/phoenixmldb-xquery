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
/// Partial application of a named function: concat(?, 'x') → function($a) { concat($a, 'x') }
/// </summary>
public sealed class PartialApplicationOperator : PhysicalOperator
{
    public required XQueryFunction? ResolvedFunc { get; init; }
    public required QName FuncName { get; init; }
    /// <summary>Null entries are placeholders (?), non-null are fixed arguments with their operators.</summary>
    public required IReadOnlyList<(int index, PhysicalOperator op)?> ArgumentSlots { get; init; }
    public required int TotalArity { get; init; }
    public required int PlaceholderCount { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate fixed arguments eagerly, materializing the full sequence for each slot.
        var fixedValues = new object?[TotalArity];
        var isPlaceholder = new bool[TotalArity];
        for (int i = 0; i < ArgumentSlots.Count; i++)
        {
            if (ArgumentSlots[i] is { } slot)
            {
                object? singleItem = null;
                List<object?>? multiItems = null;
                var count = 0;
                await foreach (var item in slot.op.ExecuteAsync(context))
                {
                    count++;
                    if (count == 1) singleItem = item;
                    else if (count == 2) multiItems = [singleItem, item];
                    else multiItems!.Add(item);
                    if (count % 65536 == 0) context.CheckMaterializationLimit(count);
                }
                fixedValues[i] = count switch
                {
                    0 => null,
                    1 => singleItem,
                    _ => multiItems!.ToArray()
                };
            }
            else
            {
                isPlaceholder[i] = true;
            }
        }

        // Resolve the target function if not resolved at compile time.
        // If resolved to a DeclaredFunctionPlaceholder, re-resolve at runtime so we get
        // the actual DeclaredFunction registered by FunctionDeclarationOperator.
        // Always re-resolve at runtime so user-declared functions (which replace their
        // DeclaredFunctionPlaceholder entries at execution time) pick up the real implementation.
        var func = context.Functions.Resolve(FuncName, TotalArity)
                   ?? (ResolvedFunc is { } rf ? context.Functions.Resolve(rf.Name, TotalArity) : null)
                   ?? ResolvedFunc;
        if (func == null)
            throw new XQueryRuntimeException("XPST0017",
                $"Cannot partially apply: function {FuncName.LocalName}#{TotalArity} not found");

        // Note: Type checking of fixed arguments happens at invocation time, not at
        // partial application time. The spec creates a new function that supplies the
        // fixed arguments when called, so numeric promotion etc. applies at call time.

        yield return new PartiallyAppliedItem(func, fixedValues, isPlaceholder, PlaceholderCount);
    }
}
