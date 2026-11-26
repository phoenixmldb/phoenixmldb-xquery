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
    /// Where the main module is, as the host said when it compiled the query
    /// (<see cref="CompilationOptions.BaseUri"/>): what <see cref="QueryExecutionContext.ModuleLocation"/>
    /// is while the main module's own code runs. A base URI the query declares is not it.
    /// </summary>
    public string? ModuleLocation { get; set; }

    /// <summary>
    /// <c>true</c> when the body of the main module is an updating expression (XQUF 3.0 §2.2): an
    /// insert, delete, replace or rename expression, a call of an updating function, an
    /// <c>invoke updating</c>, or an expression with one of these as the operand that gives its
    /// category (a branch of a conditional, the return clause of a FLWOR expression, a member of a
    /// comma expression). <c>false</c> for a simple expression, a <c>copy ... modify ... return</c>
    /// expression included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The category is static: a query that is updating can give an empty list when it runs (a
    /// conditional whose updating branch is not taken). A simple query gives no update: a dynamic
    /// call of an updating function is XUDY0038 (XQUF 3.0 §7.11), and a function test without
    /// <c>%updating</c> does not match one (§2.3.2), so a higher-order function such as
    /// <c>fn:for-each</c> does not call it (XPTY0004). The updates of a <c>copy ... modify</c>
    /// expression change only its copies.
    /// </para>
    /// <para>
    /// Set when <see cref="QueryEngine"/> compiles the query; <c>false</c> for a plan built otherwise.
    /// </para>
    /// </remarks>
    public bool IsUpdating { get; internal set; }

    /// <summary>
    /// Copy-namespaces mode declared in the prolog via <c>declare copy-namespaces ...</c>.
    /// Default is PreserveInherit.
    /// </summary>
    public Analysis.CopyNamespacesMode DeclaredCopyNamespacesMode { get; init; } =
        Analysis.CopyNamespacesMode.PreserveInherit;

    /// <summary>
    /// Construction mode declared in the prolog via <c>declare construction preserve|strip</c>.
    /// Default is Preserve.
    /// </summary>
    public Analysis.ConstructionMode DeclaredConstructionMode { get; init; } =
        Analysis.ConstructionMode.Preserve;

    /// <summary>
    /// Executes the plan and returns results.
    /// </summary>
    public async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        context.ModuleLocation = ModuleLocation ?? context.ModuleLocation;
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
                // With no base URI the declaration stays as it was written, and what is relative
                // to it names nothing. It was made absolute against the process's working
                // directory (except under a resource policy), so fn:static-base-uri() told the
                // query where the host process runs and fn:doc read the files there.
            }
            else if (string.IsNullOrEmpty(baseUri))
            {
                // Empty base-uri declaration: use existing static base URI unchanged
                baseUri = context.StaticBaseUri;
            }
            context.StaticBaseUri = baseUri;
        }
        context.CopyNamespacesMode = DeclaredCopyNamespacesMode;
        context.ConstructionMode = DeclaredConstructionMode;
        // The pending updates of the query are applied when it ends, here, for every entry point
        // (XQUF 3.0 §2.5). Only QueryEngine.ExecuteAsync applied them; the facade, the CLI and
        // every other caller of this method collected them and dropped them (BUGS.md #127).
        if (context.ImplicitTimezone is not { } zone)
        {
            await foreach (var item in Root.ExecuteGuarded(context))
            {
                yield return item;
            }
            if (!context.DeferPendingUpdates)
                PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);
            yield break;
        }

        // The timezone of this query, for the code that produces each item and for no other.
        // It is set again for each item: what an iterator sets does not stay set while its
        // caller has the item, and the caller may run another query in between.
        var items = Root.ExecuteGuarded(context).GetAsyncEnumerator(context.CancellationToken);
        await using (items.ConfigureAwait(false))
        {
            while (true)
            {
                bool more;
                using (Xdm.ImplicitTimezone.Use(zone))
                {
                    more = await items.MoveNextAsync().ConfigureAwait(false);
                }
                if (!more)
                {
                    if (!context.DeferPendingUpdates)
                        PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);
                    yield break;
                }
                yield return items.Current;
            }
        }
    }
}
