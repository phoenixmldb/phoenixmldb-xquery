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
/// Order spec operator.
/// </summary>
public sealed class OrderSpecOperator
{
    public required PhysicalOperator KeyOperator { get; init; }
    public OrderDirection Direction { get; init; }
    public EmptyOrder EmptyOrder { get; init; }
    public string? Collation { get; init; }
}
