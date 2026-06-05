using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Recognizes <c>/element[@attr = literal]</c> shapes covered by a value index
/// and emits an <see cref="IndexLookupOperator"/>. Falls through (returns null)
/// for anything it can't classify, allowing the default path planner to handle it.
/// </summary>
public sealed class IndexAwareQueryPlanOptimizer : IQueryPlanOptimizer
{
    private readonly IIndexCatalog _catalog;

    public IndexAwareQueryPlanOptimizer(IIndexCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public PhysicalOperator? OptimizePath(PathExpression path, ContainerId container)
    {
        // Look for the exact single-step shape /element[@attr = string-literal].
        if (!path.IsAbsolute) return null;
        if (path.InitialExpression != null) return null;
        if (path.Steps.Count != 1) return null;
        var step = path.Steps[0];
        if (step.Axis != Axis.Child) return null;
        if (step.NodeTest is not NameTest name) return null;
        if (name.IsLocalNameWildcard) return null;
        if (step.Predicates.Count != 1) return null;

        // Predicate must be BinaryExpression{Op=GeneralEqual, Left=PathExpression(@attr), Right=StringLiteral}.
        if (step.Predicates[0] is not BinaryExpression bin) return null;
        if (bin.Operator != BinaryOperator.GeneralEqual) return null;
        if (bin.Left is not PathExpression attrPath) return null;
        if (attrPath.IsAbsolute) return null;
        if (attrPath.InitialExpression != null) return null;
        if (attrPath.Steps.Count != 1) return null;
        var attrStep = attrPath.Steps[0];
        if (attrStep.Axis != Axis.Attribute) return null;
        if (attrStep.NodeTest is not NameTest attrName) return null;
        if (attrName.IsLocalNameWildcard) return null;
        if (attrStep.Predicates.Count != 0) return null;
        if (bin.Right is not StringLiteral literal) return null;

        var coverage = _catalog.LookupValueIndex(container, name.LocalName, attrName.LocalName);
        if (coverage == null) return null;

        // LookupAsync is wired by the Indexing-layer adapter at runtime; optimizer-side
        // we leave it null (operator yields empty) and let the adapter inject the real
        // lookup function via QueryExecutionContext.
        return new IndexLookupOperator
        {
            IndexName = coverage.IndexName,
            Key = literal.Value
        };
    }
}
