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
/// Count clause operator.
/// </summary>
public sealed class CountClauseOperator : FlworClauseOperator
{
    public required QName Variable { get; init; }
    // The counter is per execution, so it lives in FlworOperator's run state, which numbers the
    // tuples itself. A counter on this operator was shared by every concurrent execution of the
    // plan and reset by any re-entry of the FLWOR (3194 of 3200 concurrent results were wrong).
    public override IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context) =>
        throw new InvalidOperationException("A count clause is evaluated by its FlworOperator, which holds the per-execution counter.");
}
