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
/// Order by clause operator.
/// </summary>
public sealed class OrderByClauseOperator : FlworClauseOperator
{
    public bool Stable { get; init; }
    public required IReadOnlyList<OrderSpecOperator> OrderSpecs { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        // Order by is handled at the FLWOR level, not here
        // This clause just passes through
        await Task.CompletedTask;
        yield return new Dictionary<QName, object?>();
    }
}
