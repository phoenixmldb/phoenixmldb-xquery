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

    /// <summary>
    /// The content of a resource as the host's resolver supplies it, or null when there is no
    /// resolver or it leaves this resource to the engine. When the resolver is the only source
    /// (<see cref="IResourceResolver.SuppliesAllContent"/>) and does not supply it, the load is
    /// refused here: the caller must not go on to open anything.
    /// </summary>
    internal static ResourceContent? HostContent(ResourcePolicy? policy, string location, Uri? baseUri, ResourceAccessKind access)
        => HostContent(policy, location, new ResourceCaller(baseUri, null), access);

    /// <summary><see cref="HostContent(ResourcePolicy?, string, Uri?, ResourceAccessKind)"/> for a call a module makes.</summary>
    internal static ResourceContent? HostContent(ResourcePolicy? policy, string location, ResourceCaller caller, ResourceAccessKind access)
    {
        if (policy?.ResourceResolver is not { } resolver)
            return null;
        if (resolver.ResolveContent(new ResourceRequest(location, caller.BaseUri, access) { ModuleUri = caller.ModuleUri }) is { } content)
            return content;
        if (resolver.SuppliesAllContent)
            throw new ResourceAccessDeniedException(location, access,
                "the host's resource resolver, which is the only source of resources here, did not supply it");
        return null;
    }

    /// <summary>
    /// <see cref="HostContent"/> for a function running in a query, with a refusal reported as
    /// <paramref name="errorCode"/>.
    /// </summary>
    internal static ResourceContent? HostContent(Ast.ExecutionContext context, string location, ResourceAccessKind access, string errorCode)
    {
        try
        {
            return HostContent(context.ResourcePolicy, location, Caller(context), access);
        }
        catch (ResourceAccessDeniedException e)
        {
            throw new Execution.XQueryRuntimeException(errorCode, e.Message, e);
        }
    }

    /// <summary>Who is asking, for a call made by the code running in <paramref name="context"/>.</summary>
    internal static ResourceCaller Caller(Ast.ExecutionContext context) => Caller(context.StaticBaseUri, context.ModuleLocation);

    /// <inheritdoc cref="Caller(Ast.ExecutionContext)"/>
    internal static ResourceCaller Caller(string? staticBaseUri, string? moduleLocation) => new(Absolute(staticBaseUri), Absolute(moduleLocation));

    private static Uri? Absolute(string? uri) => uri != null && Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed : null;

    /// <summary>
    /// Resolves a document for a call a module makes. Under a policy the host's resolver is
    /// told who asks; any other resolver is asked as it always was.
    /// </summary>
    internal static Xdm.Nodes.XdmDocument? ResolveDocument(IDocumentResolver resolver, string uri, ResourceCaller caller) =>
        resolver is PolicyEnforcingResolver enforcing ? enforcing.ResolveDocument(uri, caller) : resolver.ResolveDocument(uri);

    /// <summary>Document availability for a call a module makes; see <see cref="ResolveDocument"/>.</summary>
    internal static bool IsDocumentAvailable(IDocumentResolver resolver, string uri, ResourceCaller caller) =>
        resolver is PolicyEnforcingResolver enforcing ? enforcing.IsDocumentAvailable(uri, caller) : resolver.IsDocumentAvailable(uri);

    /// <summary>The per-query enforcing resolver, which also keeps the load budgets.</summary>
    internal static PolicyEnforcingResolver? Resolver(Ast.ExecutionContext context) =>
        (context as Execution.QueryExecutionContext)?.DocumentResolver as PolicyEnforcingResolver;
}
