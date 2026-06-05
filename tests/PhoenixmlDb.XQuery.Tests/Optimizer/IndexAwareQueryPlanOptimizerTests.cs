using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Optimizer;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Optimizer;

public sealed class IndexAwareQueryPlanOptimizerTests
{
    [Fact]
    public void EmitsIndexLookup_WhenAttributeEqualityCoveredByValueIndex()
    {
        // Path: /book[@isbn = '978-0-13-468599-1']
        var path = BuildBookIsbnPath("book", "isbn", "978-0-13-468599-1");

        var catalog = new StubCatalog
        {
            ValueIndexFn = (_, elem, attr) =>
                elem == "book" && attr == "isbn"
                    ? new IndexCoverage("book/isbn", 0.001)
                    : null
        };
        var optimizer = new IndexAwareQueryPlanOptimizer(catalog);

        var op = optimizer.OptimizePath(path, new ContainerId(1));
        op.Should().NotBeNull();
        op.Should().BeOfType<IndexLookupOperator>();
        ((IndexLookupOperator)op!).IndexName.Should().Be("book/isbn");
        ((IndexLookupOperator)op!).Key.Should().Be("978-0-13-468599-1");
    }

    [Fact]
    public void ReturnsNull_WhenNoIndexCovers()
    {
        var path = new PathExpression
        {
            IsAbsolute = true,
            Steps = new List<StepExpression>
            {
                new StepExpression
                {
                    Axis = Axis.Child,
                    NodeTest = new NameTest { LocalName = "book" },
                    Predicates = Array.Empty<XQueryExpression>()
                }
            }
        };
        var optimizer = new IndexAwareQueryPlanOptimizer(new NullIndexCatalog());
        optimizer.OptimizePath(path, new ContainerId(1)).Should().BeNull();
    }

    [Fact]
    public void ReturnsNull_WhenPredicateIsNotAttributeEquality()
    {
        // /book[1] — positional predicate, not attribute equality
        var path = new PathExpression
        {
            IsAbsolute = true,
            Steps = new List<StepExpression>
            {
                new StepExpression
                {
                    Axis = Axis.Child,
                    NodeTest = new NameTest { LocalName = "book" },
                    Predicates = new List<XQueryExpression>
                    {
                        new IntegerLiteral { Value = 1L }
                    }
                }
            }
        };
        var catalog = new StubCatalog { ValueIndexFn = (_, _, _) => new IndexCoverage("any", 0.1) };
        var optimizer = new IndexAwareQueryPlanOptimizer(catalog);

        optimizer.OptimizePath(path, new ContainerId(1)).Should().BeNull(
            because: "positional predicates are not attribute equality");
    }

    private static PathExpression BuildBookIsbnPath(string elem, string attr, string value)
    {
        return new PathExpression
        {
            IsAbsolute = true,
            Steps = new List<StepExpression>
            {
                new StepExpression
                {
                    Axis = Axis.Child,
                    NodeTest = new NameTest { LocalName = elem },
                    Predicates = new List<XQueryExpression>
                    {
                        new BinaryExpression
                        {
                            Operator = BinaryOperator.GeneralEqual,
                            Left = new PathExpression
                            {
                                IsAbsolute = false,
                                Steps = new List<StepExpression>
                                {
                                    new StepExpression
                                    {
                                        Axis = Axis.Attribute,
                                        NodeTest = new NameTest { LocalName = attr },
                                        Predicates = Array.Empty<XQueryExpression>()
                                    }
                                }
                            },
                            Right = new StringLiteral { Value = value }
                        }
                    }
                }
            }
        };
    }

    private sealed class StubCatalog : IIndexCatalog
    {
        public Func<ContainerId, string, string, IndexCoverage?>? ValueIndexFn;
        public IndexCoverage? LookupValueIndex(ContainerId c, string elem, string attr)
            => ValueIndexFn?.Invoke(c, elem, attr);
    }
}
