using System.Collections.Frozen;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Immutable security policy controlling resource access for XSLT and XQuery engines.
/// </summary>
public sealed class ResourcePolicy
{
    public IReadOnlySet<string> AllowedSchemes { get; }
    public IReadOnlySet<string> AllowedWriteSchemes { get; }
    public IReadOnlyList<UriRule> ReadRules { get; }
    public IReadOnlyList<UriRule> WriteRules { get; }
    public IReadOnlyList<UriRule> ImportRules { get; }
    public int MaxDocumentLoads { get; }
    public int MaxResultDocuments { get; }
    public int MaxOutputSize { get; }
    public int MaxUnparsedTextLoads { get; }
    public IResourceResolver? ResourceResolver { get; }
    public bool AllowDtdProcessing { get; }
    public bool AllowXslEvaluate { get; }

    internal ResourcePolicy(
        IReadOnlySet<string> allowedSchemes,
        IReadOnlySet<string> allowedWriteSchemes,
        IReadOnlyList<UriRule> readRules,
        IReadOnlyList<UriRule> writeRules,
        IReadOnlyList<UriRule> importRules,
        int maxDocumentLoads,
        int maxResultDocuments,
        int maxOutputSize,
        int maxUnparsedTextLoads,
        IResourceResolver? resourceResolver,
        bool allowDtdProcessing,
        bool allowXslEvaluate)
    {
        AllowedSchemes = allowedSchemes;
        AllowedWriteSchemes = allowedWriteSchemes;
        ReadRules = readRules;
        WriteRules = writeRules;
        ImportRules = importRules;
        MaxDocumentLoads = maxDocumentLoads;
        MaxResultDocuments = maxResultDocuments;
        MaxOutputSize = maxOutputSize;
        MaxUnparsedTextLoads = maxUnparsedTextLoads;
        ResourceResolver = resourceResolver;
        AllowDtdProcessing = allowDtdProcessing;
        AllowXslEvaluate = allowXslEvaluate;
    }

    /// <summary>
    /// Backwards-compatible default: all schemes allowed, no limits beyond existing defaults.
    /// </summary>
    public static ResourcePolicy Unrestricted { get; } = CreateBuilder()
        .AllowScheme("*")
        .AllowWriteScheme("*")
        .AllowDtdProcessing(false)
        .AllowXslEvaluate()
        .WithMaxResultDocuments(1000)
        .WithMaxOutputSize(50 * 1024 * 1024)
        .Build();

    /// <summary>
    /// Deny-by-default for server deployments. No filesystem, no network, no DTDs, no xsl:evaluate.
    /// Only documents pre-loaded by the application or served by a custom resolver are accessible.
    /// </summary>
    public static ResourcePolicy ServerDefault { get; } = CreateBuilder()
        .WithMaxDocumentLoads(100)
        .WithMaxResultDocuments(10)
        .WithMaxOutputSize(10 * 1024 * 1024)
        .WithMaxUnparsedTextLoads(50)
        .Build();

    /// <summary>
    /// No external access at all. Only in-memory documents provided via a custom resolver.
    /// </summary>
    public static ResourcePolicy InMemoryOnly { get; } = CreateBuilder()
        .WithMaxDocumentLoads(100)
        .WithMaxResultDocuments(0)
        .WithMaxOutputSize(10 * 1024 * 1024)
        .Build();

    public static ResourcePolicyBuilder CreateBuilder() => new();

    /// <summary>
    /// Checks whether the given URI is allowed for the requested access kind.
    /// </summary>
    internal bool IsAllowed(Uri uri, ResourceAccessKind access)
    {
        // Check scheme-level allowlist
        var schemes = access.HasFlag(ResourceAccessKind.WriteDocument)
            ? AllowedWriteSchemes
            : AllowedSchemes;

        if (schemes.Count > 0 && !schemes.Contains("*") &&
            !schemes.Contains(uri.Scheme.ToLowerInvariant()))
            return false;

        // If no schemes are configured at all (empty set, no wildcard), deny by default
        if (schemes.Count == 0)
        {
            // Check rules as fallback
            var rules = access switch
            {
                _ when access.HasFlag(ResourceAccessKind.WriteDocument) => WriteRules,
                _ when access.HasFlag(ResourceAccessKind.ImportStylesheet) => ImportRules,
                _ => ReadRules
            };

            return rules.Any(r => r.Matches(uri, access));
        }

        // Check scoped rules if any exist
        var applicableRules = access switch
        {
            _ when access.HasFlag(ResourceAccessKind.WriteDocument) => WriteRules,
            _ when access.HasFlag(ResourceAccessKind.ImportStylesheet) => ImportRules,
            _ => ReadRules
        };

        // If no scoped rules, scheme allowlist is sufficient
        if (applicableRules.Count == 0)
            return true;

        return applicableRules.Any(r => r.Matches(uri, access));
    }
}
