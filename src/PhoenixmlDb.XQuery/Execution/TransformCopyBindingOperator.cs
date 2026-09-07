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
/// A copy binding in a transform expression.
/// </summary>
public sealed class TransformCopyBindingOperator
{
    public required PhoenixmlDb.Core.QName Variable { get; init; }
    public required PhysicalOperator Expression { get; init; }
}
