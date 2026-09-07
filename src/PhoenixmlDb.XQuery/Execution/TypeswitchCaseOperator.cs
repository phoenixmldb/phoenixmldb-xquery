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
/// Typeswitch case operator.
/// </summary>
public sealed class TypeswitchCaseOperator
{
    public QName? Variable { get; init; }
    public required IReadOnlyList<XdmSequenceType> Types { get; init; }
    public required PhysicalOperator Result { get; init; }
}
