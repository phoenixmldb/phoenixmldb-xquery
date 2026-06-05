using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Zero-knowledge default statistics provider. Returns conservative estimates
/// that preserve the optimizer's pre-statistics behavior — used when no
/// container-backed statistics are wired.
/// </summary>
public sealed class DefaultContainerStatistics : IContainerStatistics
{
    /// <inheritdoc />
    public long DocumentCount(ContainerId container) => 1_000;

    /// <inheritdoc />
    public long NodeCount(ContainerId container) => 100_000;

    /// <inheritdoc />
    public double AxisFanout(ContainerId container, Axis axis) => axis switch
    {
        Axis.Child or Axis.Self or Axis.Parent => 5.0,
        Axis.Attribute => 3.0,
        Axis.Descendant or Axis.DescendantOrSelf => 50.0,
        Axis.Ancestor or Axis.AncestorOrSelf => 5.0,
        Axis.Following or Axis.Preceding => 100.0,
        Axis.FollowingSibling or Axis.PrecedingSibling => 10.0,
        _ => 10.0,
    };

    /// <inheritdoc />
    public double PredicateSelectivity(ContainerId container, PredicateShape shape) => shape switch
    {
        PredicateShape.PositionalLiteral => 1.0,
        PredicateShape.PositionalRange => 0.5,
        PredicateShape.AttributeEquality => 0.1,
        PredicateShape.AttributeRange => 0.3,
        PredicateShape.NameTest => 0.2,
        PredicateShape.Unknown => 0.5,
        _ => 0.5,
    };
}
