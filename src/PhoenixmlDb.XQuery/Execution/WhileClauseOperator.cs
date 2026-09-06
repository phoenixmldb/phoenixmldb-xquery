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
/// While clause operator (XQuery 4.0). Unlike <see cref="WhereClauseOperator"/> — which
/// filters individual tuples but keeps iterating — <c>while</c> terminates the FLWOR
/// tuple stream the moment its condition first evaluates to <c>false</c>. The stream-stop
/// is enforced by <see cref="FlworOperator"/>, which inspects the condition via
/// <see cref="EvaluateConditionAsync"/> and unwinds its recursive tuple generators when it
/// returns <c>false</c>. This operator's own <see cref="ExecuteAsync"/> is a no-op
/// pass-through (it never runs directly in the streaming path — the FLWOR handles it).
/// </summary>
public sealed class WhileClauseOperator : FlworClauseOperator
{
    public required PhysicalOperator ConditionOperator { get; init; }

    /// <summary>
    /// Evaluates the while-condition's effective boolean value under the current tuple's
    /// bindings, applying the same EBV rules as <see cref="WhereClauseOperator"/>
    /// (a leading node ⇒ true; 2+ items with a non-node first ⇒ FORG0006).
    /// </summary>
    public async Task<bool> EvaluateConditionAsync(QueryExecutionContext context)
    {
        object? first = null;
        bool hasFirst = false;
        await foreach (var item in ConditionOperator.ExecuteAsync(context))
        {
            if (!hasFirst)
            {
                first = item;
                hasFirst = true;
                if (first is Xdm.Nodes.XdmNode or Xdm.TextNodeItem
                    or System.Xml.XmlNode or System.Xml.Linq.XNode)
                    return true;
            }
            else
            {
                throw new XQueryRuntimeException("FORG0006",
                    "Effective boolean value not defined for a sequence of two or more items starting with a non-node value");
            }
        }

        return QueryExecutionContext.EffectiveBooleanValue(first);
    }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        // The FLWOR operator drives while-clause evaluation directly (it must stop the
        // whole tuple stream, not just filter one tuple). If reached standalone, pass through.
        await Task.CompletedTask;
        yield return new Dictionary<QName, object?>();
    }
}
