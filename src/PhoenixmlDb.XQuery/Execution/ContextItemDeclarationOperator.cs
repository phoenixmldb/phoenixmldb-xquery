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
/// Sets the context item from a prolog declaration: declare context item := expr;
/// </summary>
public sealed class ContextItemDeclarationOperator : PhysicalOperator
{
    public PhysicalOperator? ValueOperator { get; init; }
    public XdmSequenceType? TypeConstraint { get; init; }
    public bool IsExternal { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // If external: use externally-supplied context item if available
        if (IsExternal)
        {
            // Check if there's already a context item pushed externally.
            // ContextItem returns null when the stack is empty (no focus set at all),
            // and throws XPDY0002 when AbsentFocus sentinel is on top.
            // Both cases mean no external context item was supplied.
            bool hasExternal = false;
            try
            {
                var existing = context.ContextItem;
                if (existing != null)
                {
                    hasExternal = true;
                    // External context item supplied — type check if constrained
                    if (TypeConstraint != null)
                        TypeCastHelper.RequireSequenceTypeMatch(existing, TypeConstraint, "declare context item");
                }
            }
            catch (XQueryRuntimeException ex) when (ex.ErrorCode == "XPDY0002")
            {
                // AbsentFocus sentinel — no external context
            }

            if (hasExternal)
                yield break;
            // Fall through to default value
        }

        if (ValueOperator == null)
        {
            // External with no default and no externally-supplied context
            if (IsExternal)
                throw new XQueryRuntimeException("XPDY0002", "The context item is absent");
            yield break;
        }

        // Evaluate the default value expression
        var items = new List<object?>();
        await foreach (var item in ValueOperator.ExecuteAsync(context))
            items.Add(item);

        // Context item must be exactly one item (XPTY0004 for empty or multi-item sequences)
        if (items.Count == 0)
            throw new XQueryRuntimeException("XPTY0004",
                "Context item declaration value is an empty sequence");
        if (items.Count > 1)
            throw new XQueryRuntimeException("XPTY0004",
                "Context item declaration value contains more than one item");

        var value = items[0];

        // Type check against declared type (no function conversion rules apply — XPTY0004)
        if (TypeConstraint != null)
            TypeCastHelper.RequireSequenceTypeMatch(value, TypeConstraint, "declare context item");

        context.PushContextItem(value);
        yield break;
    }
}
