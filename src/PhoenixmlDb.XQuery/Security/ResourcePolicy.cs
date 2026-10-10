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

    /// <summary>
    /// Whether a query or stylesheet may call <c>fn:transform</c>. True unless the host turns
    /// it off with <see cref="ResourcePolicyBuilder.AllowTransformFunction"/>.
    /// </summary>
    /// <remarks>
    /// <c>fn:transform</c> runs a stylesheet that the caller supplies, as a location or as
    /// text. A host that does not mean the queries it runs to run stylesheets of their own
    /// choosing turns it off. Every way to call the function then fails with FOXT0001 before
    /// the stylesheet is read, and <c>function-lookup</c> and <c>function-available</c> do not
    /// report the function.
    /// </remarks>
    public bool AllowTransformFunction { get; }

    // Schemes admitted for every read and import kind (AllowScheme). AllowedSchemes also lists
    // schemes that are only reachable through a scoped rule, so it cannot be used for decisions.
    private readonly IReadOnlySet<string> _unscopedSchemes;

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
        bool allowXslEvaluate,
        IReadOnlySet<string>? unscopedSchemes = null,
        bool allowTransformFunction = true)
    {
        _unscopedSchemes = unscopedSchemes ?? allowedSchemes;
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
        AllowTransformFunction = allowTransformFunction;
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
    /// Whether <paramref name="uri"/> may be accessed for <paramref name="access"/>. A <c>file:</c>
    /// URI is judged by its canonical path (symbolic links resolved), which is also what
    /// <see cref="Authorize"/> hands back for the reader to open.
    /// </summary>
    /// <remarks>
    /// <para>A scheme allowed with <see cref="ResourcePolicyBuilder.AllowScheme"/> (or <c>"*"</c>)
    /// admits every access kind. Otherwise access needs a rule of the requested kind: read rules
    /// (<see cref="ResourcePolicyBuilder.AllowReadFrom(string, string?, string?)"/>) admit reads
    /// only, import rules imports only, write rules writes only. An empty rule list admits
    /// nothing, so a policy that allows reading files does not also allow importing a module or
    /// stylesheet from anywhere on disk.</para>
    /// <para>A relative URI is never allowed: resolve it first (<see cref="Resolve"/>), because what
    /// matters is the resource that would actually be opened.</para>
    /// </remarks>
    public bool IsAllowed(Uri uri, ResourceAccessKind access)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            return false;
        if (uri.IsFile)
        {
            try { uri = CanonicalFileUri(uri); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
        }

        var write = (access & ResourceAccessKind.WriteDocument) != 0;
        var rules = write ? WriteRules
            : (access & ResourceAccessKind.ImportStylesheet) != 0 ? ImportRules
            : ReadRules;

        // A whole-scheme allowance applies unless a rule of this kind scopes that scheme:
        // AllowScheme("https") + AllowReadFrom("https", "api.example.com") reads only from the host.
        var unscoped = write ? AllowedWriteSchemes : _unscopedSchemes;
        if ((unscoped.Contains("*") || unscoped.Contains(uri.Scheme))
            && !rules.Any(r => r.Scheme == "*" || string.Equals(r.Scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase)))
            return true;

        foreach (var rule in rules)
        {
            if (rule.Matches(uri, access))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves <paramref name="uriOrPath"/> to the absolute URI a reader would open, checks it
    /// against this policy, and returns it. Readers must open the returned URI, not the input.
    /// </summary>
    /// <param name="uriOrPath">An absolute URI, a rooted file-system path, or a relative reference.</param>
    /// <param name="access">The kind of access requested.</param>
    /// <param name="baseUri">Base for a relative reference; without one it is resolved against the
    /// current directory, as the file readers do.</param>
    /// <exception cref="ResourceAccessDeniedException">The resource is not allowed.</exception>
    public Uri Authorize(string uriOrPath, ResourceAccessKind access, Uri? baseUri = null)
    {
        var uri = Resolve(uriOrPath, baseUri)
            ?? throw new ResourceAccessDeniedException(uriOrPath, access, "not a valid URI or path");
        if (uri.IsFile)
        {
            // The refusals below say what was asked for and never where it leads. The canonical
            // path has every symbolic link resolved; naming it let a query read the target of
            // any link on the machine out of the error it caught.
            try { uri = CanonicalFileUri(uri); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new ResourceAccessDeniedException(uriOrPath, access, "the path cannot be resolved");
            }
        }
        if (!IsAllowed(uri, access))
            throw new ResourceAccessDeniedException(uriOrPath, access, "it is not allowed by the resource policy");
        return uri;
    }

    /// <summary>As <see cref="Authorize"/>, returning <c>null</c> instead of throwing.</summary>
    public Uri? TryAuthorize(string uriOrPath, ResourceAccessKind access, Uri? baseUri = null)
    {
        try { return Authorize(uriOrPath, access, baseUri); }
        catch (ResourceAccessDeniedException) { return null; }
    }

    /// <summary>
    /// The absolute URI a reader would open for <paramref name="uriOrPath"/>: a rooted path
    /// (<c>/etc/x</c>, <c>C:\x</c>) is a <c>file:</c> URI, and a relative reference resolves
    /// against <paramref name="baseUri"/> or, without one, the current directory. Null if the
    /// input is neither a URI nor a path.
    /// </summary>
    public static Uri? Resolve(string uriOrPath, Uri? baseUri = null)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath))
            return null;
        var text = uriOrPath.Trim();
        // A rooted path is a file, whatever Uri.TryCreate makes of it: "/abs/path" parses as a
        // RELATIVE URI, which the old check waved through while the reader opened it as a file.
        if (Path.IsPathRooted(text) && !HasScheme(text))
        {
            try { return new Uri(Path.GetFullPath(text)); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException) { return null; }
        }
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) && HasScheme(text))
            return absolute;
        if (baseUri is { IsAbsoluteUri: true } && Uri.TryCreate(baseUri, text, out var resolved))
            return resolved;
        try { return new Uri(Path.GetFullPath(text)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException) { return null; }
    }

    // "c:\x" has a one-letter "scheme" to Uri; a real scheme is two or more characters.
    private static bool HasScheme(string text)
    {
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 2)
            return false;
        for (var i = 0; i < colon; i++)
        {
            var c = text[i];
            if (!(char.IsAsciiLetter(c) || (i > 0 && (char.IsAsciiDigit(c) || c is '+' or '-' or '.'))))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The <c>file:</c> URI of the canonical path: absolute, and with every symbolic link along
    /// it resolved, so a link inside an allowed directory that points outside it is judged by
    /// where it points.
    /// </summary>
    public static Uri CanonicalFileUri(Uri fileUri)
    {
        ArgumentNullException.ThrowIfNull(fileUri);
        if (!fileUri.IsFile)
            return fileUri;
        return new Uri(CanonicalPath(fileUri.LocalPath));
    }

    /// <summary>The absolute path with every symbolic link along it resolved.</summary>
    public static string CanonicalPath(string path) => Canonical(Path.GetFullPath(path), 0);

    private static string Canonical(string full, int linksFollowed)
    {
        var parent = Path.GetDirectoryName(full);
        if (parent is null)
            return full; // the root
        var canonicalParent = Canonical(parent, linksFollowed);
        var candidate = Path.Combine(canonicalParent, Path.GetFileName(full));
        FileSystemInfo info = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
        if (info.LinkTarget is not { } target)
            return candidate;
        if (linksFollowed >= 40)
            throw new IOException($"Too many levels of symbolic links: '{full}'");
        var targetPath = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(canonicalParent, target));
        return Canonical(targetPath, linksFollowed + 1);
    }
}
