using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Optimizer;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Optimizer;

/// <summary>
/// Verifies the cost comparison in QueryOptimizer's path planner:
/// an index that covers the predicate is only chosen when it is no more expensive
/// than the equivalent scan. Selectivity is driven directly onto the emitted
/// <see cref="IndexLookupOperator"/> via a stub plan optimizer so the decision is
/// deterministic and independent of any histogram (#93).
/// </summary>
public sealed class IndexVsScanCostTests
{
    [Fact]
    public void HighSelectivityIndex_LosesToScan_PlanRootIsScan()
    {
        var plan = Plan(estimatedSelectivity: 0.5);

        // A non-selective index (matches half the container) is no cheaper than a
        // scan, so the planner must fall back to axis navigation + filter.
        plan.Root.Should().NotBeOfType<IndexLookupOperator>();
        RootIsScan(plan.Root).Should().BeTrue(
            because: "a half-the-container index lookup is not cheaper than a scan");
    }

    [Fact]
    public void LowSelectivityIndex_BeatsScan_PlanRootIsIndexLookup()
    {
        var plan = Plan(estimatedSelectivity: 0.01);

        plan.Root.Should().BeOfType<IndexLookupOperator>(
            because: "a 1%-selectivity index lookup is far cheaper than a full scan");
    }

    private static ExecutionPlan Plan(double estimatedSelectivity)
    {
        // /account[@revenue > 5]
        var path = BuildAccountRevenuePath();

        var stub = new StubPlanOptimizer
        {
            EstimatedSelectivity = estimatedSelectivity,
            IndexName = "account/revenue",
            Predicate = new IndexRange(5L, false, null, false),
        };

        var stats = new StubStatistics
        {
            NodeCountFn = _ => 1000,
            // Container shaped consistently for the calibrated cost model (#93): a
            // full scan starts at DocumentCount document roots and fans out per the
            // child axis, so the scanned population is DocumentCount × fanout =
            // 50 × 20 = 1000 ≈ NodeCount (1000 indexed elements). The index basis is
            // NodeCount × selectivity. This places the index-vs-scan crossover near
            // 5% selectivity: a 1% index wins, a 50% index loses.
            DocumentCountFn = _ => 50,
            AxisFanoutFn = (_, _) => 20.0,
            PredicateSelectivityFn = (_, _) => 0.5,
        };

        var ctx = new OptimizationContext { Container = new ContainerId(1), Statistics = stats };
        var optimizer = new QueryOptimizer(stub);
        return optimizer.Optimize(path, ctx);
    }

    // The scan plan for a single predicated step is an axis navigation + filter, or —
    // when the planner routes the predicate through per-node evaluation — a
    // PerNodeStepOperator. Any of these means "the index was not chosen".
    private static bool RootIsScan(PhysicalOperator op) => op switch
    {
        FilterOperator => true,
        AxisNavigationOperator => true,
        PerNodeStepOperator => true,
        DocumentOrderSortOperator => true,
        _ => false,
    };

    private static PathExpression BuildAccountRevenuePath()
    {
        var attrPath = new PathExpression
        {
            IsAbsolute = false,
            Steps = new[]
            {
                new StepExpression
                {
                    Axis = Axis.Attribute,
                    NodeTest = new NameTest { LocalName = "revenue" },
                    Predicates = Array.Empty<XQueryExpression>(),
                }
            },
        };
        var pred = new BinaryExpression
        {
            Left = attrPath,
            Operator = BinaryOperator.GeneralGreaterThan,
            Right = new IntegerLiteral { Value = 5L },
        };
        return new PathExpression
        {
            IsAbsolute = true,
            Steps = new[]
            {
                new StepExpression
                {
                    Axis = Axis.Child,
                    NodeTest = new NameTest { LocalName = "account" },
                    Predicates = new XQueryExpression[] { pred },
                }
            },
        };
    }

    private sealed class StubPlanOptimizer : IQueryPlanOptimizer
    {
        public required double EstimatedSelectivity { get; init; }
        public required string IndexName { get; init; }
        public required IndexPredicate Predicate { get; init; }

        public PhysicalOperator? OptimizePath(PathExpression path, ContainerId container)
            => new IndexLookupOperator
            {
                IndexName = IndexName,
                Predicate = Predicate,
                EstimatedSelectivity = EstimatedSelectivity,
            };
    }

    private sealed class StubStatistics : IContainerStatistics
    {
        public Func<ContainerId, long>? NodeCountFn;
        public Func<ContainerId, long>? DocumentCountFn;
        public Func<ContainerId, Axis, double>? AxisFanoutFn;
        public Func<ContainerId, PredicateShape, double>? PredicateSelectivityFn;
        public long DocumentCount(ContainerId c) => DocumentCountFn?.Invoke(c) ?? 1;
        public long NodeCount(ContainerId c) => NodeCountFn?.Invoke(c) ?? 1;
        public double AxisFanout(ContainerId c, Axis a) => AxisFanoutFn?.Invoke(c, a) ?? 1.0;
        public double PredicateSelectivity(ContainerId c, PredicateShape s) => PredicateSelectivityFn?.Invoke(c, s) ?? 1.0;
    }
}
