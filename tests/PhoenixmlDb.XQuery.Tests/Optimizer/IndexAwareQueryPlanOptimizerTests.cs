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
            ValueIndexFn = (_, path, attr) =>
                path.Count == 1 && path[0] == "book" && attr == "isbn"
                    ? new IndexCoverage("book/isbn", 0.001)
                    : null
        };
        var optimizer = new IndexAwareQueryPlanOptimizer(catalog);

        var op = optimizer.OptimizePath(path, new ContainerId(1));
        op.Should().NotBeNull();
        op.Should().BeOfType<IndexLookupOperator>();
        ((IndexLookupOperator)op!).IndexName.Should().Be("book/isbn");
        ((IndexLookupOperator)op!).Predicate.Should().Be(new IndexEquality("978-0-13-468599-1"));
    }

    [Fact]
    public void EmitsIndexLookup_WhenAttributeEqualityComparandIsVariable()
    {
        // Path: /book[@isbn = $v]
        var path = BuildBookAttrPredicatePath(
            "book", "isbn", BinaryOperator.GeneralEqual,
            new VariableReference { Name = new QName(NamespaceId.None, "v") });

        var catalog = new StubCatalog
        {
            ValueIndexFn = (_, path, attr) =>
                path.Count == 1 && path[0] == "book" && attr == "isbn"
                    ? new IndexCoverage("book/isbn", 0.001)
                    : null
        };
        var optimizer = new IndexAwareQueryPlanOptimizer(catalog);

        var op = optimizer.OptimizePath(path, new ContainerId(1));
        op.Should().NotBeNull();
        op.Should().BeOfType<IndexLookupOperator>();
        ((IndexLookupOperator)op!).IndexName.Should().Be("book/isbn");
        ((IndexLookupOperator)op!).Predicate.Should()
            .Be(new IndexEquality(new VariableComparand(new QName(NamespaceId.None, "v"))));
    }

    [Fact]
    public void EmitsIndexRange_WhenAttributeLessThanComparandIsVariable()
    {
        // Path: /book[@price < $hi]
        var path = BuildBookAttrPredicatePath(
            "book", "price", BinaryOperator.GeneralLessThan,
            new VariableReference { Name = new QName(NamespaceId.None, "hi") });

        var catalog = new StubCatalog
        {
            ValueIndexFn = (_, path, attr) =>
                path.Count == 1 && path[0] == "book" && attr == "price"
                    ? new IndexCoverage("book/price", 0.001)
                    : null
        };
        var optimizer = new IndexAwareQueryPlanOptimizer(catalog);

        var op = optimizer.OptimizePath(path, new ContainerId(1));
        op.Should().NotBeNull();
        op.Should().BeOfType<IndexLookupOperator>();
        ((IndexLookupOperator)op!).Predicate.Should()
            .Be(new IndexRange(null, false, new VariableComparand(new QName(NamespaceId.None, "hi")), false));
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
        var catalog = new StubCatalog { ValueIndexFn = (_, _path, _attr) => new IndexCoverage("any", 0.1) };
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

    private static PathExpression BuildBookAttrPredicatePath(
        string elem, string attr, BinaryOperator op, XQueryExpression comparand)
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
                            Operator = op,
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
                            Right = comparand
                        }
                    }
                }
            }
        };
    }

    private sealed class StubCatalog : IIndexCatalog
    {
        public Func<ContainerId, IReadOnlyList<string>, string, IndexCoverage?>? ValueIndexFn;
        public IndexCoverage? LookupValueIndex(ContainerId c, IReadOnlyList<string> elementPath, string attr)
            => ValueIndexFn?.Invoke(c, elementPath, attr);
    }
}
