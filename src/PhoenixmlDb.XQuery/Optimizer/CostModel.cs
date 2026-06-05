using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Estimates execution cost and result cardinality for a physical operator tree,
/// consulting <see cref="IContainerStatistics"/> for data-driven factors.
/// </summary>
/// <remarks>
/// Both estimates are heuristics, not contracts. The cost number is unit-less —
/// only relative ordering matters. Cardinality is a long; <see cref="long.MaxValue"/>
/// indicates "unknown / unbounded" and the cost model treats it as infinite.
/// </remarks>
public sealed class CostModel
{
    private readonly IContainerStatistics _stats;

    public CostModel(IContainerStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        _stats = statistics;
    }

    /// <summary>
    /// Estimates the number of items the operator tree will produce when executed
    /// against the given container.
    /// </summary>
    public long EstimateCardinality(PhysicalOperator op, ContainerId container) => op switch
    {
        ConstantOperator => 1,
        EmptyOperator => 0,
        ContextItemOperator => 1,
        VariableOperator => 1,
        DocumentRootOperator => 1,
        AxisNavigationOperator nav => MultiplyClamped(
            EstimateCardinality(nav.Input, container),
            _stats.AxisFanout(container, nav.Axis)),
        FilterOperator filter => MultiplyClamped(
            EstimateCardinality(filter.Input, container),
            _stats.PredicateSelectivity(container, ClassifyPredicate(filter.PredicateOperator))),
        // An index lookup yields a handful of nodes on average — model with the
        // AttributeEquality selectivity against the container's node count.
        IndexLookupOperator => Math.Max(1, MultiplyClamped(
            _stats.NodeCount(container),
            _stats.PredicateSelectivity(container, PredicateShape.AttributeEquality))),
        // A per-node step inherits the input's cardinality multiplied by its axis
        // fanout, the same as an axis navigation, plus an unknown predicate filter.
        PerNodeStepOperator step => MultiplyClamped(
            MultiplyClamped(
                EstimateCardinality(step.Input, container),
                _stats.AxisFanout(container, step.Axis)),
            _stats.PredicateSelectivity(container, PredicateShape.Unknown)),
        FlworOperator => 100,
        FunctionCallOperator => 1,
        SequenceOperator seq => seq.Items.Sum(i => EstimateCardinality(i, container)),
        _ => 1,
    };

    /// <summary>
    /// Estimates the relative execution cost of the operator tree. Unit-less;
    /// only ordering matters. Larger = more expensive.
    /// </summary>
    public double EstimateCost(PhysicalOperator op, ContainerId container) => op switch
    {
        ConstantOperator => 1,
        EmptyOperator => 1,
        ContextItemOperator => 1,
        VariableOperator => 1,
        DocumentRootOperator => 10,
        // Cost of an axis nav = input cost + work done per output item. Work-per-item
        // depends on axis — descendant traversal touches more pages than child.
        AxisNavigationOperator nav => EstimateCost(nav.Input, container)
            + EstimateCardinality(nav, container) * AxisWorkPerItem(nav.Axis),
        FilterOperator filter => EstimateCost(filter.Input, container)
            + EstimateCardinality(filter.Input, container) * 1.0,
        // Index lookup is O(log n) on the B-tree plus a small per-result fetch.
        // Cost it as a small fixed seek plus per-output-node work, independent of
        // the container's total node count.
        IndexLookupOperator idx => 5.0 + EstimateCardinality(idx, container) * 0.5,
        // Per-node step = input cost + per-input-node axis work + per-output-node
        // predicate evaluation. Conservative on both fronts; predicates dominate
        // when many inputs reach the step.
        PerNodeStepOperator step => EstimateCost(step.Input, container)
            + EstimateCardinality(step.Input, container) * AxisWorkPerItem(step.Axis)
            + EstimateCardinality(step, container) * step.PredicateOperators.Count,
        FlworOperator flwor => 100 + flwor.Clauses.Sum(c => EstimateClauseCost(c, container)),
        FunctionCallOperator func => 10 + func.ArgumentOperators.Sum(a => EstimateCost(a, container)),
        BinaryOperatorNode bin => 5 + EstimateCost(bin.Left, container) + EstimateCost(bin.Right, container),
        UnaryOperatorNode unary => 2 + EstimateCost(unary.Operand, container),
        IfOperator @if => EstimateCost(@if.Condition, container)
            + Math.Max(EstimateCost(@if.Then, container), EstimateCost(@if.Else, container)),
        SequenceOperator seq => seq.Items.Sum(i => EstimateCost(i, container)),
        _ => 100,
    };

    private double EstimateClauseCost(FlworClauseOperator clause, ContainerId container) => clause switch
    {
        ForClauseOperator fc => fc.Bindings.Sum(b => EstimateCost(b.InputOperator, container)) * 10,
        LetClauseOperator lc => lc.Bindings.Sum(b => EstimateCost(b.InputOperator, container)),
        WhereClauseOperator wc => EstimateCost(wc.ConditionOperator, container),
        OrderByClauseOperator obc => obc.OrderSpecs.Sum(s => EstimateCost(s.KeyOperator, container)) * 5,
        _ => 10,
    };

    private static double AxisWorkPerItem(Axis axis) => axis switch
    {
        Axis.Child or Axis.Self or Axis.Attribute or Axis.Parent => 1.0,
        Axis.Descendant or Axis.DescendantOrSelf => 3.0,
        Axis.Following or Axis.Preceding => 5.0,
        _ => 2.0,
    };

    private static PredicateShape ClassifyPredicate(PhysicalOperator predicate) => predicate switch
    {
        ConstantOperator c when c.Value is long or int => PredicateShape.PositionalLiteral,
        _ => PredicateShape.Unknown,
    };

    private static long MultiplyClamped(long left, double factor)
    {
        if (left <= 0) return 0;
        var result = left * factor;
        if (result > long.MaxValue || double.IsInfinity(result)) return long.MaxValue;
        if (result < 1) return 0;
        return (long)Math.Round(result);
    }
}
