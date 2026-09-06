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
/// Window condition operator (start or end condition).
/// </summary>
public sealed class WindowConditionOperator
{
    public QName? CurrentItem { get; init; }
    public QName? PreviousItem { get; init; }
    public QName? NextItem { get; init; }
    public QName? Position { get; init; }
    public required PhysicalOperator WhenOperator { get; init; }
}
