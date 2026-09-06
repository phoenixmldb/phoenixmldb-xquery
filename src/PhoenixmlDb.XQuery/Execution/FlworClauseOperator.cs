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
/// Base for FLWOR clause operators.
/// </summary>
public abstract class FlworClauseOperator
{
    public abstract IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context);
}
