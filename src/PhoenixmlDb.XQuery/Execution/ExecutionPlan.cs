using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// A physical execution plan for an XQuery expression.
/// </summary>
public sealed class ExecutionPlan
{
    /// <summary>
    /// The root physical operator.
    /// </summary>
    public required PhysicalOperator Root { get; init; }

    /// <summary>
    /// The original expression (after optimization).
    /// </summary>
    public XQueryExpression? OriginalExpression { get; init; }

    /// <summary>
    /// Estimated cost of execution.
    /// </summary>
    public double EstimatedCost { get; init; }

    /// <summary>
    /// Estimated result cardinality.
    /// </summary>
    public long EstimatedCardinality { get; init; }

    /// <summary>
    /// Static base URI declared in the main module's prolog via <c>declare base-uri "..."</c>.
    /// When set, overrides the caller's StaticBaseUri at execution time.
    /// </summary>
    public string? DeclaredBaseUri { get; init; }

    /// <summary>
    /// Executes the plan and returns results.
    /// </summary>
    public async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        if (DeclaredBaseUri != null)
            context.StaticBaseUri = DeclaredBaseUri;
        await foreach (var item in Root.ExecuteAsync(context))
        {
            yield return item;
        }
    }
}
