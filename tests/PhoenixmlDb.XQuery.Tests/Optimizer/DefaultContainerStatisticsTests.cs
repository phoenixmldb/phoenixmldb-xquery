using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Optimizer;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Optimizer;

public sealed class DefaultContainerStatisticsTests
{
    [Fact]
    public void Default_ReturnsConservativeEstimate_ForUnknownContainer()
    {
        var stats = new DefaultContainerStatistics();
        var c = new ContainerId(1);

        stats.DocumentCount(c).Should().Be(1000,
            because: "default conservative estimate; production wires real LMDB-derived counts");
        stats.NodeCount(c).Should().Be(100_000,
            because: "default assumes ~100 nodes/doc when no statistics are wired");
        stats.AxisFanout(c, PhoenixmlDb.XQuery.Ast.Axis.Child).Should().Be(5.0);
        stats.AxisFanout(c, PhoenixmlDb.XQuery.Ast.Axis.Descendant).Should().Be(50.0);
        stats.PredicateSelectivity(c, PredicateShape.PositionalLiteral).Should().Be(1.0);
        stats.PredicateSelectivity(c, PredicateShape.AttributeEquality).Should().Be(0.1,
            because: "10% default selectivity for @attr='value' style predicates");
    }
}
