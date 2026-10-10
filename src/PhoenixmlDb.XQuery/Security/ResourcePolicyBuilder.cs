using System.Collections.Frozen;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Fluent builder for <see cref="ResourcePolicy"/>.
/// </summary>
public sealed class ResourcePolicyBuilder
{
    private readonly HashSet<string> _allowedSchemes = new(StringComparer.OrdinalIgnoreCase);
    // Schemes admitted for every read and import kind; see ResourcePolicy.IsAllowed.
    private readonly HashSet<string> _unscopedSchemes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _allowedWriteSchemes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<UriRule> _readRules = [];
    private readonly List<UriRule> _writeRules = [];
    private readonly List<UriRule> _importRules = [];
    private int _maxDocumentLoads;
    private int _maxResultDocuments;
    private int _maxOutputSize = 50 * 1024 * 1024;
    private int _maxUnparsedTextLoads;
    private IResourceResolver? _resourceResolver;
    private bool _allowDtdProcessing;
    private bool _allowXslEvaluate;
    private bool _allowTransformFunction = true;

    public ResourcePolicyBuilder AllowScheme(string scheme)
    {
        _allowedSchemes.Add(scheme);
        _unscopedSchemes.Add(scheme);
        return this;
    }

    public ResourcePolicyBuilder AllowWriteScheme(string scheme)
    {
        _allowedWriteSchemes.Add(scheme);
        return this;
    }

    public ResourcePolicyBuilder AllowReadFrom(string scheme, string? host = null, string? pathPrefix = null)
        => AllowReadFrom(scheme, host, pathPrefix, port: null);

    /// <summary>
    /// Allows reads (documents, text, collections) from <paramref name="scheme"/>, optionally
    /// scoped to a host, port and path prefix. Reads only: importing a module or stylesheet
    /// needs <see cref="AllowImportFrom(string, string?, string?, int?)"/>.
    /// </summary>
    /// <param name="scheme">The URI scheme, e.g. "https" or "file".</param>
    /// <param name="host">Host, or a "*.example.com" suffix pattern; null for any host.</param>
    /// <param name="pathPrefix">Path prefix matched on whole segments; null for any path.</param>
    /// <param name="port">The port; null means the scheme's default when a host is given. Use
    /// <see cref="UriRule.AnyPort"/> to allow every port on the host.</param>
    public ResourcePolicyBuilder AllowReadFrom(string scheme, string? host, string? pathPrefix, int? port)
    {
        _allowedSchemes.Add(scheme);
        _readRules.Add(new UriRule { Scheme = scheme, Host = host, PathPrefix = pathPrefix, Port = port, Access = ResourceAccessKind.AllRead });
        return this;
    }

    public ResourcePolicyBuilder AllowWriteTo(string scheme, string? host = null, string? pathPrefix = null)
    {
        // A scoped write needs a rule; only AllowWriteScheme admits a whole scheme.
        if (host == null && pathPrefix == null)
            _allowedWriteSchemes.Add(scheme);
        else
            _writeRules.Add(new UriRule { Scheme = scheme, Host = host, PathPrefix = pathPrefix, Access = ResourceAccessKind.WriteDocument });
        return this;
    }

    public ResourcePolicyBuilder AllowImportFrom(string scheme, string? host = null, string? pathPrefix = null)
        => AllowImportFrom(scheme, host, pathPrefix, port: null);

    /// <summary>
    /// Allows importing XSLT stylesheet modules, XQuery library modules and schemas from
    /// <paramref name="scheme"/>, optionally scoped to a host, port and path prefix.
    /// </summary>
    public ResourcePolicyBuilder AllowImportFrom(string scheme, string? host, string? pathPrefix, int? port)
    {
        _allowedSchemes.Add(scheme);
        _importRules.Add(new UriRule { Scheme = scheme, Host = host, PathPrefix = pathPrefix, Port = port, Access = ResourceAccessKind.ImportStylesheet });
        return this;
    }

    public ResourcePolicyBuilder WithMaxDocumentLoads(int max) { _maxDocumentLoads = max; return this; }
    public ResourcePolicyBuilder WithMaxResultDocuments(int max) { _maxResultDocuments = max; return this; }
    public ResourcePolicyBuilder WithMaxOutputSize(int max) { _maxOutputSize = max; return this; }
    public ResourcePolicyBuilder WithMaxUnparsedTextLoads(int max) { _maxUnparsedTextLoads = max; return this; }

    public ResourcePolicyBuilder WithResourceResolver(IResourceResolver resolver)
    {
        _resourceResolver = resolver;
        return this;
    }

    public ResourcePolicyBuilder AllowDtdProcessing(bool allow = true) { _allowDtdProcessing = allow; return this; }
    public ResourcePolicyBuilder AllowXslEvaluate(bool allow = true) { _allowXslEvaluate = allow; return this; }

    /// <summary>
    /// Whether <c>fn:transform</c> may be called. It may, unless this is called with false; see
    /// <see cref="ResourcePolicy.AllowTransformFunction"/>.
    /// </summary>
    public ResourcePolicyBuilder AllowTransformFunction(bool allow = true) { _allowTransformFunction = allow; return this; }

    public ResourcePolicy Build() => new(
        allowedSchemes: _allowedSchemes.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
        allowedWriteSchemes: _allowedWriteSchemes.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
        readRules: _readRules.ToArray(),
        writeRules: _writeRules.ToArray(),
        importRules: _importRules.ToArray(),
        maxDocumentLoads: _maxDocumentLoads,
        maxResultDocuments: _maxResultDocuments,
        maxOutputSize: _maxOutputSize,
        maxUnparsedTextLoads: _maxUnparsedTextLoads,
        resourceResolver: _resourceResolver,
        allowDtdProcessing: _allowDtdProcessing,
        allowXslEvaluate: _allowXslEvaluate,
        unscopedSchemes: _unscopedSchemes.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
        allowTransformFunction: _allowTransformFunction);
}
