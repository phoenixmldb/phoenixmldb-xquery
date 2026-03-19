using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Selects appropriate indexes for query predicates.
/// </summary>
public sealed class IndexSelector
{
    private readonly IIndexConfiguration _indexConfig;

    public IndexSelector(IIndexConfiguration indexConfig)
    {
        _indexConfig = indexConfig ?? throw new ArgumentNullException(nameof(indexConfig));
    }

    /// <summary>
    /// Analyzes a path expression and selects the best index strategy.
    /// </summary>
    public IndexStrategy SelectIndex(PathExpression path, ContainerId container)
    {
        var strategies = new List<IndexStrategy>();

        // Check for name index usage
        var lastStep = path.Steps.LastOrDefault();
        if (lastStep != null && lastStep.NodeTest is NameTest nameTest)
        {
            strategies.Add(new IndexStrategy
            {
                IndexType = IndexStrategyType.Name,
                LocalName = nameTest.LocalName,
                Namespace = nameTest.ResolvedNamespace.GetValueOrDefault(),
                Axis = lastStep.Axis,
                EstimatedSelectivity = EstimateNameSelectivity(nameTest.LocalName)
            });
        }

        // Check for path index usage
        var pathSteps = BuildQueryPathSteps(path);
        if (_indexConfig.ShouldIndexPath(pathSteps))
        {
            strategies.Add(new IndexStrategy
            {
                IndexType = IndexStrategyType.Path,
                QueryPathSteps = pathSteps,
                EstimatedSelectivity = EstimatePathSelectivity(pathSteps)
            });
        }

        // Analyze predicates for value index opportunities
        if (lastStep != null)
        {
            foreach (var pred in lastStep.Predicates)
            {
                var valueStrategy = AnalyzePredicateForValueIndex(pred, pathSteps);
                if (valueStrategy != null)
                {
                    strategies.Add(valueStrategy);
                }
            }
        }

        // Select the strategy with the best (lowest) selectivity
        if (strategies.Count == 0)
        {
            return new IndexStrategy
            {
                IndexType = IndexStrategyType.FullScan,
                EstimatedSelectivity = 1.0
            };
        }

        return strategies.MinBy(s => s.EstimatedSelectivity)!;
    }

    /// <summary>
    /// Analyzes a predicate to determine if it can use a value index.
    /// </summary>
    private IndexStrategy? AnalyzePredicateForValueIndex(XQueryExpression predicate, QueryPathStep[] pathSteps)
    {
        if (predicate is not BinaryExpression binExpr)
            return null;

        // Look for patterns like: @attr = "value" or . = "value"
        var (pathExpr, valueExpr) = ExtractComparisonOperands(binExpr);
        if (pathExpr == null || valueExpr == null)
            return null;

        // Check if it's an equality comparison
        if (!IsEqualityOperator(binExpr.Operator))
            return null;

        // Extract literal value
        var literalValue = ExtractLiteralValue(valueExpr);
        if (literalValue == null)
            return null;

        // Extend path with predicate path
        var predicatePath = BuildPredicatePath(pathExpr);
        var fullPath = pathSteps.Concat(predicatePath).ToArray();

        // Check if value index exists for this path
        var valueType = _indexConfig.GetValueIndexType(fullPath);
        if (valueType == null)
            return null;

        return new IndexStrategy
        {
            IndexType = IndexStrategyType.Value,
            QueryPathSteps = fullPath,
            ComparisonOperator = binExpr.Operator,
            ComparisonValue = literalValue,
            EstimatedSelectivity = EstimateValueSelectivity(literalValue)
        };
    }

    private static (XQueryExpression?, XQueryExpression?) ExtractComparisonOperands(BinaryExpression expr)
    {
        // Check left side is path, right side is literal
        if (IsPathLike(expr.Left) && IsLiteralLike(expr.Right))
        {
            return (expr.Left, expr.Right);
        }

        // Check right side is path, left side is literal
        if (IsPathLike(expr.Right) && IsLiteralLike(expr.Left))
        {
            return (expr.Right, expr.Left);
        }

        return (null, null);
    }

    private static bool IsPathLike(XQueryExpression expr)
    {
        return expr is PathExpression or StepExpression or ContextItemExpression or VariableReference;
    }

    private static bool IsLiteralLike(XQueryExpression expr)
    {
        return expr is IntegerLiteral or DecimalLiteral or DoubleLiteral or StringLiteral or BooleanLiteral;
    }

    private static bool IsEqualityOperator(BinaryOperator op)
    {
        return op is BinaryOperator.Equal or BinaryOperator.GeneralEqual;
    }

    private static object? ExtractLiteralValue(XQueryExpression expr)
    {
        return expr switch
        {
            IntegerLiteral il => il.Value,
            DecimalLiteral dl => dl.Value,
            DoubleLiteral dbl => dbl.Value,
            StringLiteral sl => sl.Value,
            BooleanLiteral bl => bl.Value,
            _ => null
        };
    }

    private static QueryPathStep[] BuildQueryPathSteps(PathExpression path)
    {
        var steps = new List<QueryPathStep>();
        foreach (var step in path.Steps)
        {
            if (step.NodeTest is NameTest nt)
            {
                steps.Add(new QueryPathStep(ConvertAxis(step.Axis), nt.ResolvedNamespace.GetValueOrDefault(), nt.LocalName));
            }
        }
        return steps.ToArray();
    }

    private static QueryPathStep[] BuildPredicatePath(XQueryExpression expr)
    {
        if (expr is ContextItemExpression)
        {
            return [];
        }

        if (expr is StepExpression step && step.NodeTest is NameTest nt)
        {
            return [new QueryPathStep(ConvertAxis(step.Axis), nt.ResolvedNamespace.GetValueOrDefault(), nt.LocalName)];
        }

        if (expr is PathExpression path)
        {
            return BuildQueryPathSteps(path);
        }

        return [];
    }

    private static QueryAxis ConvertAxis(Ast.Axis axis)
    {
        return axis switch
        {
            Ast.Axis.Child => QueryAxis.Child,
            Ast.Axis.Descendant => QueryAxis.Descendant,
            Ast.Axis.DescendantOrSelf => QueryAxis.DescendantOrSelf,
            Ast.Axis.Attribute => QueryAxis.Attribute,
            Ast.Axis.Self => QueryAxis.Self,
            Ast.Axis.Parent => QueryAxis.Parent,
            Ast.Axis.Ancestor => QueryAxis.Ancestor,
            Ast.Axis.AncestorOrSelf => QueryAxis.AncestorOrSelf,
            Ast.Axis.FollowingSibling => QueryAxis.FollowingSibling,
            Ast.Axis.PrecedingSibling => QueryAxis.PrecedingSibling,
            Ast.Axis.Following => QueryAxis.Following,
            Ast.Axis.Preceding => QueryAxis.Preceding,
            _ => QueryAxis.Child
        };
    }

    private static double EstimateNameSelectivity(string localName)
    {
        // Rough heuristics based on common element names
        return localName switch
        {
            "id" or "ID" => 0.01,  // Usually unique
            "name" or "title" => 0.1,
            "*" => 1.0,  // Wildcard matches everything
            _ => 0.05  // Default estimate
        };
    }

    private static double EstimatePathSelectivity(QueryPathStep[] steps)
    {
        // More specific paths are more selective
        var selectivity = 1.0;
        foreach (var step in steps)
        {
            selectivity *= step.LocalName == "*" ? 0.5 : 0.1;
        }
        return Math.Max(0.001, selectivity);
    }

    private static double EstimateValueSelectivity(object value)
    {
        // Value equality is usually highly selective
        return value switch
        {
            bool => 0.5,  // Binary values
            string s when s.Length == 0 => 0.1,
            string => 0.01,  // String equality is usually selective
            _ => 0.01
        };
    }
}

/// <summary>
/// Represents a selected index strategy.
/// </summary>
public sealed class IndexStrategy
{
    public IndexStrategyType IndexType { get; init; }
    public string? LocalName { get; init; }
    public NamespaceId Namespace { get; init; }
    public Ast.Axis Axis { get; init; }
    public QueryPathStep[]? QueryPathSteps { get; init; }
    public BinaryOperator ComparisonOperator { get; init; }
    public object? ComparisonValue { get; init; }
    public double EstimatedSelectivity { get; init; }
}

/// <summary>
/// Type of index strategy to use.
/// </summary>
public enum IndexStrategyType
{
    /// <summary>
    /// Full document scan (no index).
    /// </summary>
    FullScan,

    /// <summary>
    /// Name index lookup.
    /// </summary>
    Name,

    /// <summary>
    /// Path index lookup.
    /// </summary>
    Path,

    /// <summary>
    /// Value index lookup.
    /// </summary>
    Value,

    /// <summary>
    /// Full-text search index.
    /// </summary>
    FullText,

    /// <summary>
    /// Structural index for parent/child navigation.
    /// </summary>
    Structural
}
