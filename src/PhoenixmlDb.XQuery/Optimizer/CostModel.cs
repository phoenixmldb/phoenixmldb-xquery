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

    /// <summary>Fixed B-tree seek cost charged once per index lookup.</summary>
    private const double IndexSeekCost = 5.0;

    /// <summary>
    /// Per-matching-row cost of an index lookup. Materially larger than the scan's
    /// per-row <see cref="AxisWorkPerItem"/> (1.0) because each index hit pays a
    /// document load + predicate re-validation, while a scan row is a sequential
    /// read. Calibrated against <c>SelectivityPlanChoiceTests</c> so the
    /// index-vs-scan crossover lands near 5% selectivity (#93).
    /// </summary>
    private const double IndexCostPerMatchingRow = 40.0;

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
        // A document-root step is the entry point of a full container scan: it
        // yields one root node per document, so its cardinality is the container's
        // document count, NOT a single node. Modeling it as 1 made every scan plan
        // cost-independent of container size (it never scaled past one document's
        // fan-out), so a selective index could never beat a scan over a large
        // container. Scaling it here makes a scan over a big container expensive,
        // which is exactly what lets a selective index win (#93).
        DocumentRootOperator root => Math.Max(1, _stats.DocumentCount(root.Container)),
        AxisNavigationOperator nav => MultiplyClamped(
            EstimateCardinality(nav.Input, container),
            _stats.AxisFanout(container, nav.Axis)),
        FilterOperator filter => MultiplyClamped(
            EstimateCardinality(filter.Input, container),
            _stats.PredicateSelectivity(container, ClassifyPredicate(filter.PredicateOperator))),
        // An index lookup's cardinality. When the catalog supplied a value-specific
        // selectivity (e.g. from a histogram), use it directly against the node count —
        // this is what lets the planner notice an index that matches half the container
        // is no cheaper than a scan. Otherwise fall back to the predicate-shape constant
        // (range is less selective than equality).
        IndexLookupOperator idx => idx.EstimatedSelectivity is double sel
            ? Math.Max(1, MultiplyClamped(_stats.NodeCount(container), sel))
            : Math.Max(1, MultiplyClamped(
                _stats.NodeCount(container),
                _stats.PredicateSelectivity(container,
                    idx.Predicate is IndexRange ? PredicateShape.AttributeRange : PredicateShape.AttributeEquality))),
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
        // Index lookup is O(log n) on the B-tree plus a per-result fetch, but each
        // matching result is far more expensive than a sequential scan row: the
        // index hit must load the matching document and re-validate the predicate
        // against the live node, whereas a scan reads nodes sequentially off
        // already-resident pages. So the per-matching-row constant is deliberately
        // an order of magnitude larger than the scan's per-row AxisWork (1.0). With
        // a full scan modeled as DocumentCount × child-fanout (≈container size) and
        // an index sized as NodeCount × selectivity, this constant places the
        // index-vs-scan crossover at ≈5% selectivity (#93): below it the index's
        // small matching set wins; above it the per-row premium makes a sequential
        // scan cheaper. See SelectivityPlanChoiceTests for the calibration oracle.
        IndexLookupOperator idx => IndexSeekCost + EstimateCardinality(idx, container) * IndexCostPerMatchingRow,
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
