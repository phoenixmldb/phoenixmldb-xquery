using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Optimizer;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Optimizer;

public sealed class IndexRangeMultiStepTests
{
    private sealed class StubCatalog : IIndexCatalog
    {
        public Func<ContainerId, IReadOnlyList<string>, string, IndexCoverage?>? Fn;
        public IndexCoverage? LookupValueIndex(ContainerId c, IReadOnlyList<string> elementPath, string attr)
            => Fn?.Invoke(c, elementPath, attr);
    }

    private static PathExpression BuildPredicatePath(string elem, string attr, BinaryOperator op, string literal, bool attrOnLeft = true)
    {
        var attrPath = new PathExpression
        {
            IsAbsolute = false,
            Steps = new[] { new StepExpression { Axis = Axis.Attribute, NodeTest = new NameTest { LocalName = attr }, Predicates = Array.Empty<XQueryExpression>() } },
        };
        var lit = new StringLiteral { Value = literal };
        var bin = attrOnLeft
            ? new BinaryExpression { Left = attrPath, Operator = op, Right = lit }
            : new BinaryExpression { Left = lit, Operator = op, Right = attrPath };
        return new PathExpression
        {
            IsAbsolute = true,
            Steps = new[] { new StepExpression { Axis = Axis.Child, NodeTest = new NameTest { LocalName = elem }, Predicates = new XQueryExpression[] { bin } } },
        };
    }

    private static PathExpression BuildPredicatePathNumeric(string elem, string attr, BinaryOperator op, XQueryExpression literal, bool attrOnLeft = true)
    {
        var attrPath = new PathExpression
        {
            IsAbsolute = false,
            Steps = new[] { new StepExpression { Axis = Axis.Attribute, NodeTest = new NameTest { LocalName = attr }, Predicates = Array.Empty<XQueryExpression>() } },
        };
        var bin = attrOnLeft
            ? new BinaryExpression { Left = attrPath, Operator = op, Right = literal }
            : new BinaryExpression { Left = literal, Operator = op, Right = attrPath };
        return new PathExpression
        {
            IsAbsolute = true,
            Steps = new[] { new StepExpression { Axis = Axis.Child, NodeTest = new NameTest { LocalName = elem }, Predicates = new XQueryExpression[] { bin } } },
        };
    }

    private static StubCatalog CoveringCatalog(string elem, string attr)
        => new() { Fn = (_, path, a) => path.Count == 1 && path[0] == elem && a == attr ? new IndexCoverage($"{elem}/{attr}", 0.1) : null };

    [Theory]
    [InlineData(BinaryOperator.GeneralLessThan)]
    [InlineData(BinaryOperator.GeneralLessOrEqual)]
    [InlineData(BinaryOperator.GeneralGreaterThan)]
    [InlineData(BinaryOperator.GeneralGreaterOrEqual)]
    [InlineData(BinaryOperator.LessThan)]
    [InlineData(BinaryOperator.LessOrEqual)]
    [InlineData(BinaryOperator.GreaterThan)]
    [InlineData(BinaryOperator.GreaterOrEqual)]
    public void EmitsIndexRange_ForComparisonOperators(BinaryOperator op)
    {
        var path = BuildPredicatePath("book", "price", op, "100");
        var plan = new IndexAwareQueryPlanOptimizer(CoveringCatalog("book", "price")).OptimizePath(path, new ContainerId(1));
        plan.Should().BeOfType<IndexLookupOperator>();
        ((IndexLookupOperator)plan!).Predicate.Should().BeOfType<IndexRange>();
    }

    [Fact]
    public void LessThan_SetsUpperBoundExclusive()
    {
        var path = BuildPredicatePath("book", "price", BinaryOperator.GeneralLessThan, "100");
        var range = (IndexRange)((IndexLookupOperator)new IndexAwareQueryPlanOptimizer(CoveringCatalog("book", "price")).OptimizePath(path, new ContainerId(1))!).Predicate;
        range.Lower.Should().BeNull();
        range.Upper.Should().Be("100");
        range.UpperInclusive.Should().BeFalse();
    }

    [Fact]
    public void GreaterOrEqual_SetsLowerBoundInclusive()
    {
        var path = BuildPredicatePath("book", "price", BinaryOperator.GeneralGreaterOrEqual, "5");
        var range = (IndexRange)((IndexLookupOperator)new IndexAwareQueryPlanOptimizer(CoveringCatalog("book", "price")).OptimizePath(path, new ContainerId(1))!).Predicate;
        range.Lower.Should().Be("5");
        range.LowerInclusive.Should().BeTrue();
        range.Upper.Should().BeNull();
    }

    [Fact]
    public void LiteralOnLeft_FlipsComparison()
    {
        // 100 > @price  ==  @price < 100
        var path = BuildPredicatePath("book", "price", BinaryOperator.GeneralGreaterThan, "100", attrOnLeft: false);
        var range = (IndexRange)((IndexLookupOperator)new IndexAwareQueryPlanOptimizer(CoveringCatalog("book", "price")).OptimizePath(path, new ContainerId(1))!).Predicate;
        range.Upper.Should().Be("100");
        range.UpperInclusive.Should().BeFalse();
        range.Lower.Should().BeNull();
    }

    [Fact]
    public void Equality_StillEmitsIndexEquality()
    {
        var path = BuildPredicatePath("book", "isbn", BinaryOperator.GeneralEqual, "x");
        ((IndexLookupOperator)new IndexAwareQueryPlanOptimizer(CoveringCatalog("book", "isbn")).OptimizePath(path, new ContainerId(1))!).Predicate
            .Should().Be(new IndexEquality("x"));
    }

    [Fact]
    public void IntegerLiteral_EmitsIndexRange_WithLongUpperBound()
    {
        // /book[@price < 100] — integer literal (long 100L), not a string literal
        var intLit = new IntegerLiteral { Value = 100L };
        var path = BuildPredicatePathNumeric("book", "price", BinaryOperator.LessThan, intLit);
        var plan = new IndexAwareQueryPlanOptimizer(CoveringCatalog("book", "price")).OptimizePath(path, new ContainerId(1));
        plan.Should().BeOfType<IndexLookupOperator>();
        var range = ((IndexLookupOperator)plan!).Predicate.Should().BeOfType<IndexRange>().Subject;
        range.Lower.Should().BeNull();
        range.UpperInclusive.Should().BeFalse();
        range.Upper.Should().Be(100L);
    }
}
