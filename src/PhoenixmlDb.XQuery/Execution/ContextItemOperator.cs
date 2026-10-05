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
/// Returns the current context item.
/// </summary>
public sealed class ContextItemOperator : PhysicalOperator
{
    /// <summary>
    /// Set when the planner supplies this as the implicit input of a relative path's first step
    /// (`a/b` starts from the context item). A step whose input is that is an axis step on the
    /// context item: a non-node there is XPTY0020. Any other input is the left operand of `/`,
    /// where a non-node is XPTY0019 (XPath 3.1 §3.3.1.1), including an explicit `.` in `./a`.
    /// </summary>
    public bool IsImplicitStepInput { get; init; }

    internal override bool SupportsSync => true;

    internal override object? EvaluateSync(QueryExecutionContext context)
        => context.ContextItem ?? throw new XQueryRuntimeException("XPDY0002", "The context item is absent");

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        var item = context.ContextItem;
        if (item == null)
            throw new XQueryRuntimeException("XPDY0002", "The context item is absent");
        yield return item;
    }
}
