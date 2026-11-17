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
/// For binding operator.
/// </summary>
public sealed class ForBindingOperator
{
    public required QName Variable { get; init; }
    public QName? PositionalVariable { get; init; }
    /// <summary>for $x score $s in …: bound to each item's relevance (Full Text 3.0 [49]).</summary>
    public QName? ScoreVariable { get; init; }
    public bool AllowingEmpty { get; init; }
    public required PhysicalOperator InputOperator { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
}
