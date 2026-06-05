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
}
