using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Extension point for database-layer query optimization.
/// Allows the database engine to inject index-aware plan rewrites
/// without the XQuery engine depending on index infrastructure.
/// </summary>
public interface IQueryPlanOptimizer
{
    /// <summary>
    /// Attempts to create an optimized operator for a path expression.
    /// Returns null if no optimization is available.
    /// </summary>
    PhysicalOperator? OptimizePath(PathExpression path, ContainerId container);
}
