using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Optimizer;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Optimizer;

public sealed class CostModelTests
{
    [Fact]
    public void Cardinality_AxisNavigation_MultipliesInputByAxisFanout()
    {
        var stats = new DefaultContainerStatistics();
        var c = new ContainerId(1);
        var model = new CostModel(stats);

        var op = new AxisNavigationOperator
        {
            Input = new DocumentRootOperator { Container = c },
            Axis = Axis.Child,
            NodeTest = null!
        };

        // DocumentRoot cardinality = DocumentCount (default 1000), child fanout = 5.0
        // → 1000 × 5 = 5000. A full scan begins at every document's root, so the
        // entry cardinality is the container's document count (#93).
        model.EstimateCardinality(op, c).Should().Be(5000);
    }

    [Fact]
    public void Cardinality_NestedAxisNavigation_AppliesFanoutPerStep()
    {
        var stats = new DefaultContainerStatistics();
        var c = new ContainerId(1);
        var model = new CostModel(stats);

        var rootToChild = new AxisNavigationOperator
        {
            Input = new DocumentRootOperator { Container = c },
            Axis = Axis.Child,
            NodeTest = null!
        };
        var childToDescendant = new AxisNavigationOperator
        {
            Input = rootToChild,
            Axis = Axis.Descendant,
            NodeTest = null!
        };

        // 1000 (doc count = root cardinality) × 5 (child fanout) × 50 (descendant
        // fanout) = 250000 (#93: DocumentRoot cardinality is the container's
        // document count, not 1).
        model.EstimateCardinality(childToDescendant, c).Should().Be(250000);
    }

    [Fact]
    public void Cardinality_Filter_AppliesUnknownSelectivity_WhenShapeNotClassified()
    {
        var stats = new DefaultContainerStatistics();
        var c = new ContainerId(1);
        var model = new CostModel(stats);

        var input = new AxisNavigationOperator
        {
            Input = new DocumentRootOperator { Container = c },
            Axis = Axis.Descendant,
            NodeTest = null!
        };

        var filter = new FilterOperator
        {
            Input = input,
            PredicateOperator = new ConstantOperator { Value = true },
            RequiresPositionalAccess = false
        };

        // Input: DocumentRoot card = DocumentCount (default 1000) × descendant
        // fanout 50 = 50000; PredicateShape.Unknown → selectivity 0.5 → 25000
        // (#93: DocumentRoot cardinality is the container's document count).
        model.EstimateCardinality(filter, c).Should().Be(25000);
    }

    [Fact]
    public void Cost_AxisNavigation_ScalesWithCardinality()
    {
        var stats = new DefaultContainerStatistics();
        var c = new ContainerId(1);
        var model = new CostModel(stats);

        var small = new AxisNavigationOperator
        {
            Input = new DocumentRootOperator { Container = c },
            Axis = Axis.Child,
            NodeTest = null!
        };

        var deeper = new AxisNavigationOperator
        {
            Input = small,
            Axis = Axis.Descendant,
            NodeTest = null!
        };

        // Both produce real numbers; the deeper plan must cost more.
        model.EstimateCost(deeper, c).Should().BeGreaterThan(model.EstimateCost(small, c));
    }

    [Fact]
    public void QueryOptimizer_UsesContextStatistics_ForCardinality()
    {
        var stub = new StubStatistics
        {
            NodeCountFn = _ => 50_000,
            DocumentCountFn = _ => 100,
            AxisFanoutFn = (_, _) => 7.0,  // distinct from default 5.0
            PredicateSelectivityFn = (_, _) => 0.5,
        };
        var c = new ContainerId(1);

        // Build a tiny AST: /root/child
        var expr = new PhoenixmlDb.XQuery.Ast.PathExpression
        {
            IsAbsolute = true,
            Steps = new[]
            {
                new PhoenixmlDb.XQuery.Ast.StepExpression
                {
                    Axis = PhoenixmlDb.XQuery.Ast.Axis.Child,
                    NodeTest = new PhoenixmlDb.XQuery.Ast.NameTest { LocalName = "root" },
                    Predicates = Array.Empty<PhoenixmlDb.XQuery.Ast.XQueryExpression>(),
                },
                new PhoenixmlDb.XQuery.Ast.StepExpression
                {
                    Axis = PhoenixmlDb.XQuery.Ast.Axis.Child,
                    NodeTest = new PhoenixmlDb.XQuery.Ast.NameTest { LocalName = "child" },
                    Predicates = Array.Empty<PhoenixmlDb.XQuery.Ast.XQueryExpression>(),
                },
            }.ToList(),
        };

        var ctx = new OptimizationContext { Container = c, Statistics = stub };
        var optimizer = new QueryOptimizer();
        var plan = optimizer.Optimize(expr, ctx);

        // DocumentRoot = DocumentCount (stub 100), child fanout 7.0 ×2
        // → 100 × 7 × 7 = 4900 (#93: DocumentRoot cardinality is the document count).
        plan.EstimatedCardinality.Should().Be(4900);
    }

    [Fact]
    public void Cardinality_IndexLookup_UsesEstimatedSelectivity_OverNodeCount()
    {
        var stub = new StubStatistics
        {
            NodeCountFn = _ => 1000,
            // Constant fallback would multiply by these; assert they are NOT used.
            PredicateSelectivityFn = (_, _) => 0.1,
        };
        var c = new ContainerId(1);
        var model = new CostModel(stub);

        var op = new IndexLookupOperator
        {
            IndexName = "x",
            Predicate = new IndexEquality("v"),
            EstimatedSelectivity = 0.5,
        };

        // 1000 nodes × 0.5 selectivity = 500 (not the 0.1-constant path's 100).
        model.EstimateCardinality(op, c).Should().Be(500);
    }

    [Fact]
    public void Cardinality_IndexLookup_FallsBackToPredicateShapeConstant_WhenSelectivityNull()
    {
        var stub = new StubStatistics
        {
            NodeCountFn = _ => 1000,
            PredicateSelectivityFn = (_, shape) =>
                shape == PredicateShape.AttributeEquality ? 0.1 : 0.3,
        };
        var c = new ContainerId(1);
        var model = new CostModel(stub);

        var op = new IndexLookupOperator
        {
            IndexName = "x",
            Predicate = new IndexEquality("v"),
            EstimatedSelectivity = null,
        };

        // Falls back to AttributeEquality constant: 1000 × 0.1 = 100.
        model.EstimateCardinality(op, c).Should().Be(100);
    }

    [Fact]
    public void Cardinality_IndexLookup_RangeFallsBackToRangeConstant_WhenSelectivityNull()
    {
        var stub = new StubStatistics
        {
            NodeCountFn = _ => 1000,
            PredicateSelectivityFn = (_, shape) =>
                shape == PredicateShape.AttributeRange ? 0.3 : 0.1,
        };
        var c = new ContainerId(1);
        var model = new CostModel(stub);

        var op = new IndexLookupOperator
        {
            IndexName = "x",
            Predicate = new IndexRange(5L, false, null, false),
            EstimatedSelectivity = null,
        };

        // Falls back to AttributeRange constant: 1000 × 0.3 = 300.
        model.EstimateCardinality(op, c).Should().Be(300);
    }

    private sealed class StubStatistics : IContainerStatistics
    {
        public Func<ContainerId, long>? DocumentCountFn;
        public Func<ContainerId, long>? NodeCountFn;
        public Func<ContainerId, PhoenixmlDb.XQuery.Ast.Axis, double>? AxisFanoutFn;
        public Func<ContainerId, PredicateShape, double>? PredicateSelectivityFn;
        public long DocumentCount(ContainerId c) => DocumentCountFn?.Invoke(c) ?? 1;
        public long NodeCount(ContainerId c) => NodeCountFn?.Invoke(c) ?? 1;
        public double AxisFanout(ContainerId c, PhoenixmlDb.XQuery.Ast.Axis a) => AxisFanoutFn?.Invoke(c, a) ?? 1.0;
        public double PredicateSelectivity(ContainerId c, PredicateShape s) => PredicateSelectivityFn?.Invoke(c, s) ?? 1.0;
    }
}
