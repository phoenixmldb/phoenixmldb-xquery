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

        // DocumentRoot cardinality = 1, child fanout = 5.0 → 5
        model.EstimateCardinality(op, c).Should().Be(5);
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

        // 1 (root) × 5 (child fanout) × 50 (descendant fanout) = 250
        model.EstimateCardinality(childToDescendant, c).Should().Be(250);
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

        // Input cardinality 50; PredicateShape.Unknown → selectivity 0.5 → 25
        model.EstimateCardinality(filter, c).Should().Be(25);
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

        // DocumentRoot=1, child fanout 7.0 ×2 = 49
        plan.EstimatedCardinality.Should().Be(49);
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
