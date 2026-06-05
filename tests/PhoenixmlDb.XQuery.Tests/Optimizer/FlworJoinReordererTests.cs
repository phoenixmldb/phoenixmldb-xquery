using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Optimizer;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Optimizer;

public sealed class FlworJoinReordererTests
{
    [Fact]
    public void Reorder_SingleBinding_ReturnsAsIs()
    {
        var only = new ForBinding
        {
            Variable = new QName(NamespaceId.None, "a"),
            Expression = new IntegerLiteral { Value = 1L }
        };
        var cm = new CostModel(new DefaultContainerStatistics());
        var reorderer = new FlworJoinReorderer(cm);
        var reordered = reorderer.Reorder(new[] { only }, new ContainerId(1));
        reordered.Should().ContainSingle().Which.Should().BeSameAs(only);
    }

    [Fact]
    public void Reorder_IndependentBindings_PutsCheaperFirst()
    {
        // "Expensive": absolute path with descendant axis -> wide fanout.
        var expensive = new ForBinding
        {
            Variable = new QName(NamespaceId.None, "a"),
            Expression = new PathExpression
            {
                IsAbsolute = true,
                Steps = new List<StepExpression>
                {
                    new()
                    {
                        Axis = Axis.Descendant,
                        NodeTest = new NameTest { LocalName = "x" },
                        Predicates = new List<XQueryExpression>()
                    }
                }
            }
        };
        // "Cheap": integer literal — constant cost.
        var cheap = new ForBinding
        {
            Variable = new QName(NamespaceId.None, "b"),
            Expression = new IntegerLiteral { Value = 1L }
        };

        var cm = new CostModel(new DefaultContainerStatistics());
        var reorderer = new FlworJoinReorderer(cm);
        var reordered = reorderer.Reorder(new[] { expensive, cheap }, new ContainerId(1));

        reordered[0].Variable.LocalName.Should().Be("b",
            because: "cheaper binding should drive the outer loop");
        reordered[1].Variable.LocalName.Should().Be("a");
    }

    [Fact]
    public void Reorder_DependentBindings_PreservesOrder()
    {
        // $a is expensive, $b references $a -> $a MUST come first regardless of cost.
        var aBinding = new ForBinding
        {
            Variable = new QName(NamespaceId.None, "a"),
            Expression = new PathExpression
            {
                IsAbsolute = true,
                Steps = new List<StepExpression>
                {
                    new()
                    {
                        Axis = Axis.Descendant,
                        NodeTest = new NameTest { LocalName = "x" },
                        Predicates = new List<XQueryExpression>()
                    }
                }
            }
        };
        // $b's expression references $a — $a must come first
        var bBinding = new ForBinding
        {
            Variable = new QName(NamespaceId.None, "b"),
            Expression = new VariableReference { Name = new QName(NamespaceId.None, "a") }
        };

        var cm = new CostModel(new DefaultContainerStatistics());
        var reorderer = new FlworJoinReorderer(cm);
        var reordered = reorderer.Reorder(new[] { aBinding, bBinding }, new ContainerId(1));

        reordered[0].Variable.LocalName.Should().Be("a",
            because: "$b references $a; data dependency requires $a first");
        reordered[1].Variable.LocalName.Should().Be("b");
    }
}
