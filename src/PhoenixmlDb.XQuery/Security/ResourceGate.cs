namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// The one check every function that dereferences a URI or path makes before opening it.
/// </summary>
internal static class ResourceGate
{
    /// <summary>
    /// Authorises <paramref name="uriOrPath"/> under the context's resource policy and returns the
    /// URI to open (absolute; a <c>file:</c> path canonicalised). Returns null when no policy is
    /// configured, in which case the caller proceeds as it always has. A denial is raised as
    /// <paramref name="errorCode"/>, the error the function reports for an unretrievable resource.
    /// </summary>
    internal static Uri? Authorize(Ast.ExecutionContext context, string uriOrPath, ResourceAccessKind access, string errorCode)
    {
        if (context.ResourcePolicy is not { } policy)
            return null;
        var baseUri = context.StaticBaseUri is { } b && Uri.TryCreate(b, UriKind.Absolute, out var parsed) ? parsed : null;
        try
        {
            return policy.Authorize(uriOrPath, access, baseUri);
        }
        catch (ResourceAccessDeniedException e)
        {
            throw new Execution.XQueryRuntimeException(errorCode, e.Message, e);
        }
    }

    /// <summary>The per-query enforcing resolver, which also keeps the load budgets.</summary>
    internal static PolicyEnforcingResolver? Resolver(Ast.ExecutionContext context) =>
        (context as Execution.QueryExecutionContext)?.DocumentResolver as PolicyEnforcingResolver;
}
