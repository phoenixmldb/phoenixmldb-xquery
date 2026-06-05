using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Provides cardinality and selectivity estimates for a container, consumed by
/// the cost model to score alternative execution plans.
/// </summary>
/// <remarks>
/// Implementations are expected to be cheap to query — the optimizer calls these
/// methods many times per plan estimation. Cache derived counts; don't scan
/// LMDB pages on every call. The optimizer treats returned values as estimates,
/// not contracts: bounds may be wrong, the planner just picks whichever option
/// scores cheapest. Worst case is a suboptimal plan, never incorrect results.
/// </remarks>
public interface IContainerStatistics
{
    /// <summary>Approximate number of documents in the container.</summary>
    long DocumentCount(ContainerId container);

    /// <summary>Approximate total node count (across all documents).</summary>
    long NodeCount(ContainerId container);

    /// <summary>
    /// Average fan-out for an axis step. Used by the cost model to
    /// estimate cardinality after an axis navigation. E.g., Child ~ 5 means
    /// each node has on average 5 children; Descendant ~ 50 means each node
    /// has 50 descendants.
    /// </summary>
    double AxisFanout(ContainerId container, Axis axis);

    /// <summary>
    /// Selectivity factor for a predicate shape — the fraction of input nodes
    /// expected to pass the predicate. <c>1.0</c> means "all pass", <c>0.0</c>
    /// means "none pass". Used to estimate cardinality reduction after a filter.
    /// </summary>
    double PredicateSelectivity(ContainerId container, PredicateShape shape);
}

/// <summary>Coarse classification of predicate shapes for selectivity lookup.</summary>
public enum PredicateShape
{
    /// <summary>e.g., <c>[1]</c>, <c>[2]</c> — selects one item per input.</summary>
    PositionalLiteral,
    /// <summary>e.g., <c>[position() &lt; 10]</c>.</summary>
    PositionalRange,
    /// <summary>e.g., <c>[@id='123']</c>.</summary>
    AttributeEquality,
    /// <summary>e.g., <c>[@price &lt; 100]</c>.</summary>
    AttributeRange,
    /// <summary>e.g., <c>[name() = 'foo']</c>.</summary>
    NameTest,
    /// <summary>Any other shape we can't classify.</summary>
    Unknown,
}
