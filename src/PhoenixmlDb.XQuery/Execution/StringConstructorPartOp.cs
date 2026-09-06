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
/// A part of a string constructor operator: either a literal string or an expression operator.
/// </summary>
public sealed class StringConstructorPartOp
{
    public string? LiteralValue { get; init; }
    public PhysicalOperator? ExpressionOperator { get; init; }
}
