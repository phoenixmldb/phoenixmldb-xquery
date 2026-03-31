using System.Collections.Frozen;

namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Fluent builder for <see cref="ResourcePolicy"/>.
/// </summary>
public sealed class ResourcePolicyBuilder
{
    private readonly HashSet<string> _allowedSchemes = new(StringComparer.OrdinalIgnoreCase);
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

    public ResourcePolicyBuilder AllowScheme(string scheme)
    {
        _allowedSchemes.Add(scheme);
        return this;
    }

    public ResourcePolicyBuilder AllowWriteScheme(string scheme)
    {
        _allowedWriteSchemes.Add(scheme);
        return this;
    }

    public ResourcePolicyBuilder AllowReadFrom(string scheme, string? host = null, string? pathPrefix = null)
    {
        _allowedSchemes.Add(scheme);
        if (host != null || pathPrefix != null)
            _readRules.Add(new UriRule { Scheme = scheme, Host = host, PathPrefix = pathPrefix, Access = ResourceAccessKind.AllRead });
        return this;
    }

    public ResourcePolicyBuilder AllowWriteTo(string scheme, string? host = null, string? pathPrefix = null)
    {
        _allowedWriteSchemes.Add(scheme);
        if (host != null || pathPrefix != null)
            _writeRules.Add(new UriRule { Scheme = scheme, Host = host, PathPrefix = pathPrefix, Access = ResourceAccessKind.WriteDocument });
        return this;
    }

    public ResourcePolicyBuilder AllowImportFrom(string scheme, string? host = null, string? pathPrefix = null)
    {
        _allowedSchemes.Add(scheme);
        if (host != null || pathPrefix != null)
            _importRules.Add(new UriRule { Scheme = scheme, Host = host, PathPrefix = pathPrefix, Access = ResourceAccessKind.ImportStylesheet });
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
        allowXslEvaluate: _allowXslEvaluate);
}
