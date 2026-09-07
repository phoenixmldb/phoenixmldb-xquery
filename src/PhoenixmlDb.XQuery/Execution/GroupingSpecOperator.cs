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
/// Grouping spec operator.
/// </summary>
public sealed class GroupingSpecOperator
{
    public required QName Variable { get; init; }
    public PhysicalOperator? KeyOperator { get; init; }
    public Ast.XdmSequenceType? TypeDeclaration { get; init; }
    public string? Collation { get; init; }
}
