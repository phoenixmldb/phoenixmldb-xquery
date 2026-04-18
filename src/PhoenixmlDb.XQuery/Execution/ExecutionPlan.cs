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
    /// Copy-namespaces mode declared in the prolog via <c>declare copy-namespaces ...</c>.
    /// Default is PreserveInherit.
    /// </summary>
    public Analysis.CopyNamespacesMode DeclaredCopyNamespacesMode { get; init; } =
        Analysis.CopyNamespacesMode.PreserveInherit;

    /// <summary>
    /// Executes the plan and returns results.
    /// </summary>
    public async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        if (DeclaredBaseUri != null)
        {
            // Resolve relative base URIs against the existing static base URI or cwd.
            // A URI is considered absolute if it has a scheme (e.g., http:, file:, urn:).
            var baseUri = DeclaredBaseUri;
            bool isAbsolute = !string.IsNullOrEmpty(baseUri) &&
                System.Text.RegularExpressions.Regex.IsMatch(baseUri, @"^[a-zA-Z][a-zA-Z0-9+\-.]*:");
            if (!string.IsNullOrEmpty(baseUri) && !isAbsolute)
            {
                var existingBase = context.StaticBaseUri;
                if (existingBase != null && Uri.TryCreate(existingBase, UriKind.Absolute, out var existingUri))
                {
                    if (Uri.TryCreate(existingUri, baseUri, out var resolved))
                        baseUri = resolved.AbsoluteUri;
                }
                else
                {
                    // Fall back to current working directory
                    var cwdUri = new Uri(Environment.CurrentDirectory + "/");
                    if (Uri.TryCreate(cwdUri, baseUri, out var resolved))
                        baseUri = resolved.AbsoluteUri;
                }
            }
            else if (string.IsNullOrEmpty(baseUri))
            {
                // Empty base-uri declaration: use existing static base URI unchanged
                baseUri = context.StaticBaseUri;
            }
            context.StaticBaseUri = baseUri;
        }
        context.CopyNamespacesMode = DeclaredCopyNamespacesMode;
        await foreach (var item in Root.ExecuteAsync(context))
        {
            yield return item;
        }
    }
}
