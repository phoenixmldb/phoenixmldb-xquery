using System.Xml;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Core.Schema;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// <see cref="ISchemaProvider"/> implementation backed by <see cref="XmlSchemaSet"/>.
/// Provides full XSD validation, type annotations, and schema-element/attribute matching.
/// </summary>
/// <remarks>
/// Schema documents are read by the shared schema layer, <see cref="PhoenixmlDb.Core.Schema"/>:
/// each document of a schema once, the whole closure before anything is compiled, within that
/// layer's limits (16 MiB a document, 64 MiB and 1,024 documents a schema, 512 levels of
/// nesting, no document type declaration), and with its XSD 1.1 compatibility. A document the
/// schema refers to that cannot be read fails the load; it is not skipped.
/// </remarks>
public sealed class XsdSchemaProvider : ISchemaProvider
{
    // Nothing is fetched by the set itself: every document comes from the schema layer's read.
    private XmlSchemaSet _schemas;
    private readonly object _sync = new();

    /// <summary>
    /// The loads that made the set, when the set is one the <see cref="ImportCache"/> compiled
    /// and other providers may share. Such a set is never changed: a further load gets another
    /// set from the cache, or the provider builds a set of its own from these loads.
    /// </summary>
    private List<(string? TargetNamespace, Uri Root, Security.ResourcePolicy? Policy)>? _sharedLoads;

    /// <summary>
    /// Where the compiled schemas of <c>import schema</c> are kept, for every provider in the
    /// process: a schema read from files is compiled once and used until one of its documents
    /// changes, however many queries import it. Each use checks the size and time of every
    /// document of the schema, and asks the resource policy of that use about each one.
    /// </summary>
    /// <remarks>
    /// Only a schema whose root is a file and whose policy has no resource resolver is kept here.
    /// A host that supplies schema documents itself compiles them through
    /// <see cref="SchemaCompiler"/> or a cache of its own and uses
    /// <see cref="XsdSchemaProvider(CompiledSchema)"/>.
    /// </remarks>
    public static SchemaCache ImportCache { get; } = new(new SchemaCacheOptions { CheckInterval = TimeSpan.Zero });

    /// <summary>
    /// True when the set is a <see cref="CompiledSchema"/>'s, which other users share: nothing
    /// may be added to it.
    /// </summary>
    private readonly bool _fixed;

    private int _textSchemaCount;

    /// <summary>
    /// Maps NamespaceId values seen in inbound XdmQName parameters back to namespace URIs.
    /// Populated as schemas are loaded (built-in XSD/XML/XSI ids registered up-front, and any
    /// arbitrary URI's hash-based id added on first encounter via <see cref="RememberNamespaceId"/>).
    /// Solves the lossy NamespaceId-from-URI hashing problem for the QName-based lookup methods —
    /// the URI-string overloads sidestep this entirely and should be preferred where possible.
    /// </summary>
    private readonly Dictionary<NamespaceId, string> _namespaceUriById = new()
    {
        [NamespaceId.None] = "",
        [NamespaceId.Xsd] = "http://www.w3.org/2001/XMLSchema",
        [NamespaceId.Xml] = "http://www.w3.org/XML/1998/namespace",
        [NamespaceId.Xsi] = "http://www.w3.org/2001/XMLSchema-instance",
    };

    private TimeSpan? _patternMatchTimeout;
    private long _patternMatchTimeoutTicks;
    private readonly HashSet<string> _checkedPatterns = new(StringComparer.Ordinal);
    private readonly HashSet<string> _checkedLiterals = new(StringComparer.Ordinal);

    /// <summary>
    /// The most time one match of an XSD <c>pattern</c> facet may take, in a cast to a schema
    /// type, in validation and while a schema compiles. Default: <c>null</c>, which leaves
    /// .NET's process-wide default (infinite unless the host sets it).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A host that loads schemas it did not write, or validates values it did not write against
    /// patterns that may backtrack, should set this before adding a schema: compiling a schema
    /// already matches the schema's own enumeration, default and fixed values against its patterns.
    /// A match that runs past the limit throws
    /// <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/> from the provider's
    /// own methods; a query or transformation reports it as <c>FOER0000</c>, or as cancellation
    /// when it was cancelled meanwhile. A schema whose own values run past it is refused with
    /// <c>XQST0059</c>.
    /// </para>
    /// <para>
    /// The engine lowers this to <see cref="Execution.QueryExecutionLimits.RegexMatchTimeout"/>
    /// for a query that runs with one, and it stays lowered: the patterns belong to the schema
    /// set, which every query on this provider shares.
    /// </para>
    /// <para>
    /// This replaces the expressions System.Xml compiled, which it keeps in private state. On a
    /// runtime where they cannot be reached, setting the limit on a schema that declares a
    /// pattern throws <see cref="NotSupportedException"/> instead of leaving it unbounded.
    /// </para>
    /// </remarks>
    public TimeSpan? PatternMatchTimeout
    {
        get => _patternMatchTimeout;
        set
        {
            if (value is { } limit && limit <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "The match timeout must be positive.");
            lock (_sync)
            {
                if (_fixed && (value is not { } tighter || (_patternMatchTimeout is { } held && tighter > held)))
                    throw new InvalidOperationException(
                        "This provider's schema is a compiled schema that others share; its pattern time limit can be lowered, not raised or removed.");
                if (_sharedLoads is { } shared)
                {
                    // The limit is part of what the shared set was compiled with: another limit
                    // is another set, and this one is left as its other users have it.
                    _patternMatchTimeout = value;
                    Volatile.Write(ref _patternMatchTimeoutTicks, value?.Ticks ?? 0);
                    _sharedLoads = null;
                    _schemas = new XmlSchemaSet { XmlResolver = null };
                    if (!TryLoadShared(shared))
                    {
                        foreach (var (targetNamespace, root, policy) in shared)
                            LoadOwn(targetNamespace, root, null, policy, root.AbsoluteUri);
                    }
                    return;
                }
                if (_schemas.IsCompiled)
                    SchemaPatternGuard.Bound(_schemas, value ?? System.Text.RegularExpressions.Regex.InfiniteMatchTimeout);
                _patternMatchTimeout = value;
                Volatile.Write(ref _patternMatchTimeoutTicks, value?.Ticks ?? 0);
            }
        }
    }

    /// <inheritdoc />
    public void LimitPatternMatchTime(TimeSpan timeout)
    {
        // Read without the lock: this runs for every query, and a stale read only repeats the work.
        var ticks = Volatile.Read(ref _patternMatchTimeoutTicks);
        if (ticks > 0 && ticks <= timeout.Ticks)
            return;
        lock (_sync)
        {
            if (_patternMatchTimeout is { } now && now <= timeout)
                return;
            PatternMatchTimeout = timeout;
        }
    }

    /// <summary>
    /// Creates an empty schema provider. Use <see cref="ImportSchema(string, IReadOnlyList{string})"/> or <see cref="Add(string)"/>
    /// to load schemas.
    /// </summary>
    public XsdSchemaProvider() => _schemas = new XmlSchemaSet { XmlResolver = null };

    /// <summary>
    /// Creates a schema provider on a schema the shared schema layer compiled, from
    /// <see cref="SchemaCompiler"/> or a <see cref="SchemaCache"/>. A host that runs many queries
    /// against the same schemas compiles them once and gives each engine a provider on the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The compiled schema is shared and does not change, so nothing can be added to this
    /// provider: an <c>import schema</c> for a namespace the schema declares is satisfied by it,
    /// and one for any other namespace is an error (<c>XQST0059</c>), as is <see cref="Add(string)"/>.
    /// </para>
    /// <para>
    /// The pattern time limit is the one the schema was compiled with
    /// (<see cref="SchemaCompileOptions.PatternMatchTimeout"/>). A query that runs with a shorter
    /// <see cref="Execution.QueryExecutionLimits.RegexMatchTimeout"/> lowers it, for every user
    /// of the compiled schema, and it stays lowered.
    /// </para>
    /// </remarks>
    public XsdSchemaProvider(CompiledSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schemas = schema.SchemaSet;
        _fixed = true;
        _patternMatchTimeout = schema.PatternMatchTimeout;
        _patternMatchTimeoutTicks = schema.PatternMatchTimeout?.Ticks ?? 0;
        foreach (var ns in EnumerateLoadedNamespaces())
            RememberNamespaceId(ns);
    }

    /// <summary>
    /// A catalog that says where the documents a schema names are really read from; see
    /// <see cref="SchemaCompileOptions.Catalog"/>. Null, the default, for none. What it maps a
    /// name to is still read under the resource policy of the load.
    /// </summary>
    public XmlCatalog? Catalog { get; set; }

    /// <summary>
    /// Creates a schema provider with one or more XSD files pre-loaded.
    /// </summary>
    public XsdSchemaProvider(params string[] schemaFiles) : this()
    {
        ArgumentNullException.ThrowIfNull(schemaFiles);
        foreach (var file in schemaFiles)
            Add(file);
    }

    /// <summary>
    /// Loads an XSD schema from a file path.
    /// </summary>
    public void Add(string schemaPath)
    {
        ArgumentNullException.ThrowIfNull(schemaPath);
        Load(null, LocationUri(schemaPath), null, null, $"Failed to load schema from '{schemaPath}'");
        // Track every namespace the schema set now exposes so QName-keyed lookups work
        // for all URIs the caller might query.
        foreach (var ns in EnumerateLoadedNamespaces())
            RememberNamespaceId(ns);
    }

    /// <summary>
    /// Loads an XSD schema from a <see cref="TextReader"/>.
    /// </summary>
    /// <remarks>
    /// The text has no location, so a relative <c>schemaLocation</c> in it names nothing and the
    /// load fails; use <see cref="AddFromString(string, string, Uri, Security.ResourcePolicy?)"/>
    /// to say where the text is from.
    /// </remarks>
    public void Add(string targetNamespace, TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        AddText(targetNamespace, reader.ReadToEnd(), null, null);
    }

    private void AddText(string targetNamespace, string text, Uri? baseUri, Security.ResourcePolicy? policy)
    {
        if (baseUri is { IsAbsoluteUri: false })
            throw new ArgumentException("The schema text's base URI must be absolute.", nameof(baseUri));
        // Each text is a document of its own to the schema set, which takes two documents of one
        // name for the same document and does not load the second (and a fragment is no part of
        // a name to it). So the name is the base URI with a suffix of ours: what the text refers
        // to by a relative location still resolves as it does against the base. With no base URI
        // the name is not a location, and nothing resolves against it.
        var number = Interlocked.Increment(ref _textSchemaCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var uri = baseUri is null
            ? new Uri("urn:phoenixmldb:schema-text:" + number)
            : new Uri(baseUri.GetLeftPart(UriPartial.Path) + ".schema-text-" + number);
        Load(targetNamespace, uri, SchemaSource.FromText(text, uri), policy,
            $"Failed to load schema for namespace '{targetNamespace}'");
        RememberNamespaceId(targetNamespace);
    }

    /// <summary>
    /// Loads an XSD schema from an inline string.
    /// </summary>
    public void AddFromString(string targetNamespace, string xsdContent)
    {
        Add(targetNamespace, new StringReader(xsdContent));
    }

    /// <summary>
    /// Loads an XSD schema from text, fetching the schema documents it refers to
    /// (<c>xs:include</c>, <c>xs:import</c>, <c>xs:redefine</c>) only where
    /// <paramref name="policy"/> allows imports.
    /// </summary>
    /// <remarks>
    /// The overloads without a policy resolve those references with no restriction: any file the
    /// process can read and any URL it can reach, redirects included. A host that has vetted the
    /// schema text itself, but not what that text points to, should use this one;
    /// <see cref="Security.ResourcePolicy.InMemoryOnly"/> resolves nothing at all.
    /// </remarks>
    public void Add(string targetNamespace, TextReader reader, Security.ResourcePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(policy);
        AddText(targetNamespace, reader.ReadToEnd(), null, policy);
    }

    /// <summary>
    /// Loads an XSD schema from an inline string under <paramref name="policy"/>; see
    /// <see cref="Add(string, TextReader, Security.ResourcePolicy)"/>.
    /// </summary>
    public void AddFromString(string targetNamespace, string xsdContent, Security.ResourcePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(xsdContent);
        ArgumentNullException.ThrowIfNull(policy);
        AddText(targetNamespace, xsdContent, null, policy);
    }

    /// <summary>
    /// Loads an XSD schema from text that came from <paramref name="baseUri"/>: a relative
    /// <c>schemaLocation</c> in it is resolved against that URI. The documents it refers to are
    /// read under <paramref name="policy"/>, or with no restriction when it is null.
    /// </summary>
    public void AddFromString(string targetNamespace, string xsdContent, Uri baseUri, Security.ResourcePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(xsdContent);
        ArgumentNullException.ThrowIfNull(baseUri);
        AddText(targetNamespace, xsdContent, baseUri, policy);
    }

    /// <inheritdoc />
    public void AddSchemaText(string targetNamespace, string schemaText, Uri? baseUri, Security.ResourcePolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(schemaText);
        AddText(targetNamespace, schemaText, baseUri, policy);
    }

    private static Uri LocationUri(string location) =>
        Security.ResourcePolicy.Resolve(location, null)
        ?? (Uri.TryCreate(location, UriKind.Absolute, out var absolute) ? absolute : new Uri(Path.GetFullPath(location)));

    /// <summary>
    /// Reads one schema — the root and everything it includes, imports or redefines — through
    /// the schema layer, adds it to the set and compiles the set.
    /// </summary>
    private void Load(string? targetNamespace, Uri root, SchemaSource? source, Security.ResourcePolicy? policy,
        string failure)
    {
        if (_fixed)
            throw new SchemaException("XQST0059",
                $"{failure}: this provider was made from a compiled schema, which is shared and cannot be added to.");
        lock (_sync)
        {
            // A schema from files, with nothing in the set that the cache did not compile: the
            // compiled schema is shared. Whatever it cannot give (a failure, most of all) is
            // left to the provider's own load below, which reports it.
            if (source is null && Catalog is null && root.IsFile && policy?.ResourceResolver is null
                && (_sharedLoads is not null ? ReferenceEquals(_sharedLoads[0].Policy, policy) : _schemas.Count == 0))
            {
                var loads = new List<(string?, Uri, Security.ResourcePolicy?)>(_sharedLoads ?? []) { (targetNamespace, root, policy) };
                if (TryLoadShared(loads))
                    return;
            }
            OwnTheSet();
            LoadOwn(targetNamespace, root, source, policy, failure);
        }
    }

    /// <summary>
    /// Takes the compiled schema of <paramref name="loads"/> from the <see cref="ImportCache"/>
    /// as this provider's set. False, with nothing changed, when the cache cannot give one.
    /// </summary>
    private bool TryLoadShared(List<(string? TargetNamespace, Uri Root, Security.ResourcePolicy? Policy)> loads)
    {
        CompiledSchema compiled;
        try
        {
            var options = _patternMatchTimeout is { } limit
                ? new SchemaCompileOptions { PatternMatchTimeout = limit }
                : null;
            compiled = ImportCache.Get(loads.Select(load => load.Root).Distinct(), new PolicySchemaGate(loads[0].Policy, versioned: true), null, options);
        }
        catch (Exception ex) when (ex is SchemaCompilationException or XmlSchemaException or XmlException or IOException
                                       or UnauthorizedAccessException or Security.ResourceAccessDeniedException or NotSupportedException)
        {
            return false;
        }
        foreach (var (targetNamespace, _, _) in loads)
        {
            if (targetNamespace is not null && !compiled.SchemaSet.Contains(targetNamespace))
                return false;
        }
        _schemas = compiled.SchemaSet;
        _sharedLoads = loads;
        return true;
    }

    /// <summary>
    /// Replaces a shared set with one of this provider's own that holds the same schemas, so
    /// that something can be added to it.
    /// </summary>
    private void OwnTheSet()
    {
        if (_sharedLoads is not { } loads)
            return;
        _sharedLoads = null;
        _schemas = new XmlSchemaSet { XmlResolver = null };
        foreach (var (targetNamespace, root, policy) in loads)
            LoadOwn(targetNamespace, root, null, policy, root.AbsoluteUri);
    }

    private void LoadOwn(string? targetNamespace, Uri root, SchemaSource? source, Security.ResourcePolicy? policy,
        string failure)
    {
        var gate = new PolicySchemaGate(policy);
        try
        {
            var documents = SchemaCompiler.Read([root], gate, source is null ? null : [source],
                Catalog is null ? null : new SchemaCompileOptions { Catalog = Catalog });
            var notLoaded = documents.AddTo(_schemas, targetNamespace);
            if (notLoaded.Count > 0)
                throw new SchemaException("XQST0059", $"{failure}: {notLoaded[0].Message}");
            CompileSchemas();
        }
        catch (SchemaCompilationException ex)
        {
            RemoveUncompiled();
            // The gate knows why a document was not available; the layer only that it was not.
            throw new SchemaException("XQST0059",
                gate.Failures.Count > 0 ? $"{failure}: {string.Join("; ", gate.Failures)}" : $"{failure}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is XmlSchemaException or XmlException)
        {
            RemoveUncompiled();
            throw new SchemaException("XQST0059", $"{failure}: {ex.Message}", ex);
        }
    }

    /// <summary>Whether the set is one the <see cref="ImportCache"/> compiled.</summary>
    internal bool UsesSharedSet => _sharedLoads is not null;

    /// <summary>The set, for a test that two providers hold the same one.</summary>
    internal XmlSchemaSet SchemaSetForTest => _schemas;

    /// <summary>Leaves nothing in the set from a load that failed.</summary>
    private void RemoveUncompiled()
    {
        foreach (var pending in _schemas.Schemas().Cast<XmlSchema>().Where(schema => !schema.IsCompiled).ToList())
            _schemas.Remove(pending);
    }

    /// <summary>
    /// The schema layer's gate for this provider: a document is read where the resource policy
    /// of the load allows imports — from the host's resolver first — or, for the overloads that
    /// take no policy, wherever the process can read.
    /// </summary>
    private sealed class PolicySchemaGate(Security.ResourcePolicy? policy, bool versioned = false) : ISchemaAccessGate
    {
        private readonly XmlResolver _resolver = policy is null
            ? new XmlUrlResolver()
            : new Security.PolicyXmlResolver(policy, Security.ResourceAccessKind.ImportStylesheet);

        public string Identity => policy is null ? "xquery-schema-provider" : "xquery-schema-provider:policy";

        /// <summary>
        /// The size and time of a file the policy allows, which is what the import cache
        /// compares. Null for anything else: such a document is read again for each use.
        /// </summary>
        private string? FileVersion(Uri uri)
        {
            if (!versioned || !uri.IsFile)
                return null;
            try
            {
                var path = uri.LocalPath;
                if (policy is not null)
                {
                    if (policy.TryAuthorize(uri.AbsoluteUri, Security.ResourceAccessKind.ImportStylesheet) is not { } allowed)
                        return null;
                    path = allowed.LocalPath;
                }
                var file = new FileInfo(path);
                return file.Exists
                    ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{file.Length}-{file.LastWriteTimeUtc.Ticks}")
                    : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>Why each document that was not read was not, as "location: reason".</summary>
        public List<string> Failures { get; } = [];

        // The resolver's sources answer at once or block: the provider's own methods are synchronous.
        public ValueTask<SchemaDocumentContent?> OpenAsync(SchemaDocumentRequest request, CancellationToken cancellationToken)
            => new(Open(request));

        private SchemaDocumentContent? Open(SchemaDocumentRequest request)
        {
            try
            {
                // The version is taken before the read: a file that changes meanwhile has
                // another version at the next look, and the schema is compiled again.
                var version = FileVersion(request.Uri);
                if (_resolver.GetEntity(request.Uri, null, typeof(Stream)) is Stream stream)
                    return new SchemaDocumentContent(stream, version ?? "unversioned");
                Failures.Add($"{request.Location}: not available");
            }
            catch (Security.ResourceAccessDeniedException ex)
            {
                Failures.Add($"The resource policy refused a schema document this schema refers to: {request.Uri.AbsoluteUri} ({ex.Message})");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException
                                           or System.Net.Http.HttpRequestException or System.Net.WebException
                                           or UriFormatException or NotSupportedException)
            {
                Failures.Add($"{request.Location}: {ex.Message}");
            }
            return null;
        }

        public ValueTask<string?> GetVersionAsync(SchemaDocumentRequest request, CancellationToken cancellationToken)
            => new(FileVersion(request.Uri));
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.ImportSchema
    // ──────────────────────────────────────────────

    /// <summary>
    /// Compiles the schema set, first keeping a single schema for the XML namespace. Two schemas
    /// that each import xml.xsd from a different location add two copies of it, and compiling
    /// fails with "The global attribute 'xml:lang' has already been declared" although the copies
    /// declare the same things. The QT3 Catalog schemas do exactly this.
    /// </summary>
    private void CompileSchemas()
    {
        // Keep the MOST COMPLETE copy, not the first one enumerated. .NET adds its own built-in
        // schema for the namespace when an import names it without a location, and that one
        // declares no xml:id. The set's enumeration order varies by runtime, so keeping "the
        // first" sometimes discarded a host's fuller copy: xml:id became undeclared and the
        // whole import failed (xslt BuiltinXmlNamespaceSchemaTests("id"), on some runtimes).
        var xmlNamespaceSchemas = _schemas.Schemas("http://www.w3.org/XML/1998/namespace").Cast<XmlSchema>()
            .OrderByDescending(schema => schema.Items.Count)
            .ToList();
        foreach (var duplicate in xmlNamespaceSchemas.Skip(1))
            _schemas.Remove(duplicate);
        if (_patternMatchTimeout is not { } limit)
        {
            _schemas.Compile();
            return;
        }
        lock (_sync)
        {
            try
            {
                SchemaPatternGuard.CheckSchemaLiterals(_schemas, limit, _checkedPatterns, _checkedLiterals);
            }
            catch (SchemaCompilationException ex)
            {
                // Leave nothing behind that a later compile would run without the check.
                RemoveUncompiled();
                throw new SchemaException("XQST0059", ex.Message, ex);
            }
            _schemas.Compile();
            // Compiling rebuilds every type's patterns, the ones bounded before included.
            SchemaPatternGuard.Bound(_schemas, limit);
        }
    }

    private const string FnNamespace = "http://www.w3.org/2005/xpath-functions";

    /// <summary>Adapts a prefix lookup to System.Xml's resolver interface for ParseValue.</summary>
    private sealed class PrefixResolver(Func<string, string?>? resolve) : IXmlNamespaceResolver
    {
        public IDictionary<string, string> GetNamespacesInScope(XmlNamespaceScope scope) => new Dictionary<string, string>();
        public string? LookupNamespace(string prefix) =>
            prefix == "xml" ? "http://www.w3.org/XML/1998/namespace" : resolve?.Invoke(prefix);
        public string? LookupPrefix(string namespaceName) => null;
    }

    public bool HasSchemaType(string? namespaceUri, string localName) =>
        _schemas.GlobalTypes[new XmlQualifiedName(localName, namespaceUri ?? "")] is XmlSchemaType;

    /// <summary>
    /// The NamespaceId of a schema type's namespace, as type annotations carry it. Shared by the
    /// parser (element(*, T)) and the annotating parse, so the two compare equal.
    /// </summary>
    public static NamespaceId TypeNamespaceId(string namespaceUri) =>
        namespaceUri == "http://www.w3.org/2001/XMLSchema"
            ? NamespaceId.Xsd
            : new NamespaceId((uint)namespaceUri.GetHashCode(StringComparison.Ordinal));

    /// <summary>
    /// Adds the built-in schema for the XML representation of JSON (F&amp;O 3.1 §17.1), the type
    /// system of fn:json-to-xml's result. False if the resource is missing from the build.
    /// </summary>
    internal bool TryAddBuiltInJsonSchema()
    {
        if (HasNamespace(FnNamespace))
            return true;
        if (_fixed)
            return false;
        using var stream = typeof(XsdSchemaProvider).Assembly.GetManifestResourceStream("PhoenixmlDb.XQuery.schema-for-json.xsd");
        if (stream is null)
            return false;
        OwnTheSet();
        using var reader = XmlReader.Create(stream);
        _schemas.Add(FnNamespace, reader);
        CompileSchemas();
        RememberNamespaceId(FnNamespace);
        return true;
    }

    /// <inheritdoc />
    public void ImportSchema(string targetNamespace, IReadOnlyList<string>? locationHints, Security.ResourcePolicy? policy)
        => ImportSchemaCore(targetNamespace, locationHints, policy);

    public void ImportSchema(string targetNamespace, IReadOnlyList<string>? locationHints = null)
        => ImportSchemaCore(targetNamespace, locationHints, null);

    private void ImportSchemaCore(string targetNamespace, IReadOnlyList<string>? locationHints, Security.ResourcePolicy? policy)
    {
        if (HasNamespace(targetNamespace))
            return;

        // Why each hint failed. Without it the error below says "Cannot locate schema" for a
        // schema that was located perfectly well and then failed to COMPILE — which sends the
        // next reader looking for a missing file. Same failure mode as the type-name
        // diagnostics: a message naming a cause it never established.
        List<string>? attempts = null;
        if (locationHints is { Count: > 0 })
        {
            foreach (var hint in locationHints)
            {
                try
                {
                    Load(targetNamespace, LocationUri(hint), null, policy, hint);
                    RememberNamespaceId(targetNamespace);
                    return;
                }
                // A hint that cannot be read (missing, refused, unreachable, not XML) is one more
                // failed attempt, reported as XQST0059 below — not a raw I/O exception.
                catch (Exception ex) when (ex is SchemaException or UriFormatException or ArgumentException)
                {
                    (attempts ??= []).Add(ex.Message);
                }
            }
        }

        // The fn namespace's schema for fn:json-to-xml output is built in: F&O 3.1 expects the
        // processor to recognize the namespace without a location (QT3 json-to-xml-017b etc.).
        if (attempts is null && targetNamespace == FnNamespace && TryAddBuiltInJsonSchema())
            return;

        throw new SchemaException("XQST0059",
            attempts is null
                ? $"No schema location was given for namespace '{targetNamespace}'"
                : $"Could not load a schema for namespace '{targetNamespace}'. Tried "
                  + string.Join("; ", attempts));
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.IsSubtypeOf
    // ──────────────────────────────────────────────

    public bool IsSubtypeOf(XdmTypeName actualType, XdmTypeName requiredType)
    {
        if (actualType == requiredType)
            return true;

        if (requiredType == XdmTypeName.AnyType)
            return true;

        if (requiredType == XdmTypeName.AnySimpleType)
        {
            var schemaType = FindSchemaType(actualType);
            return schemaType is XmlSchemaSimpleType;
        }

        // Walk the XSD derivation chain
        var actual = FindSchemaType(actualType);
        var required = FindSchemaType(requiredType);
        if (actual == null || required == null)
            return false;

        var current = actual;
        while (current != null)
        {
            // Two anonymous types both have the empty name; only identity tells them apart.
            if (ReferenceEquals(current, required)
                || (!current.QualifiedName.IsEmpty && current.QualifiedName == required.QualifiedName))
                return true;
            current = current.BaseXmlSchemaType;
        }

        return false;
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.HasElementDeclaration / HasAttributeDeclaration
    // ──────────────────────────────────────────────

    public bool HasElementDeclaration(XdmQName name)
        => _schemas.GlobalElements.Contains(ToXmlQualifiedName(name));

    public bool HasAttributeDeclaration(XdmQName name)
        => _schemas.GlobalAttributes.Contains(ToXmlQualifiedName(name));

    public bool HasElementDeclaration(string namespaceUri, string localName)
        => _schemas.GlobalElements.Contains(new XmlQualifiedName(localName, namespaceUri ?? ""));

    public bool HasAttributeDeclaration(string namespaceUri, string localName)
        => _schemas.GlobalAttributes.Contains(new XmlQualifiedName(localName, namespaceUri ?? ""));

    // ──────────────────────────────────────────────
    //  ISchemaProvider.GetElementType / GetAttributeType
    // ──────────────────────────────────────────────

    public XdmTypeName? GetElementType(XdmQName name)
    {
        if (_schemas.GlobalElements[ToXmlQualifiedName(name)] is XmlSchemaElement elem
            && elem.ElementSchemaType != null)
            return ToXdmTypeName(elem.ElementSchemaType);
        return null;
    }

    public XdmTypeName? GetAttributeType(XdmQName name)
    {
        if (_schemas.GlobalAttributes[ToXmlQualifiedName(name)] is XmlSchemaAttribute attr
            && attr.AttributeSchemaType != null)
            return ToXdmTypeName(attr.AttributeSchemaType);
        return null;
    }

    public XdmTypeName? GetElementType(string namespaceUri, string localName)
    {
        if (_schemas.GlobalElements[new XmlQualifiedName(localName, namespaceUri ?? "")] is XmlSchemaElement elem
            && elem.ElementSchemaType != null)
            return ToXdmTypeName(elem.ElementSchemaType);
        return null;
    }

    public XdmTypeName? GetAttributeType(string namespaceUri, string localName)
    {
        if (_schemas.GlobalAttributes[new XmlQualifiedName(localName, namespaceUri ?? "")] is XmlSchemaAttribute attr
            && attr.AttributeSchemaType != null)
            return ToXdmTypeName(attr.AttributeSchemaType);
        return null;
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.MatchesSchemaElement / MatchesSchemaAttribute
    // ──────────────────────────────────────────────

    public bool MatchesSchemaElement(XdmElement element, XdmQName declarationName)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (_schemas.GlobalElements[ToXmlQualifiedName(declarationName)] is not XmlSchemaElement decl)
            return false;

        // Check direct name match
        if (element.LocalName == declarationName.LocalName
            && element.Namespace == declarationName.Namespace)
        {
            if (decl.ElementSchemaType != null)
            {
                var declaredType = ToXdmTypeName(decl.ElementSchemaType);
                return IsSubtypeOf(element.TypeAnnotation, declaredType);
            }
            return true;
        }

        // Check substitution group members
        return IsInSubstitutionGroup(element, decl);
    }

    public bool MatchesSchemaElement(string elementNamespaceUri, string elementLocalName, XdmTypeName typeAnnotation,
        string declarationNamespaceUri, string declarationLocalName)
    {
        var declarationName = new XmlQualifiedName(declarationLocalName, declarationNamespaceUri ?? "");
        if (_schemas.GlobalElements[declarationName] is not XmlSchemaElement declaration)
            return false;
        var actualName = new XmlQualifiedName(elementLocalName, elementNamespaceUri ?? "");
        if (actualName != declarationName && !SubstitutesFor(actualName, declarationName))
            return false;
        return AnnotationDerivesFrom(typeAnnotation, declaration.ElementSchemaType);
    }

    public bool MatchesSchemaAttribute(string attributeNamespaceUri, string attributeLocalName, XdmTypeName typeAnnotation,
        string declarationNamespaceUri, string declarationLocalName)
    {
        var declarationName = new XmlQualifiedName(declarationLocalName, declarationNamespaceUri ?? "");
        if (_schemas.GlobalAttributes[declarationName] is not XmlSchemaAttribute declaration)
            return false;
        if (new XmlQualifiedName(attributeLocalName, attributeNamespaceUri ?? "") != declarationName)
            return false;
        return AnnotationDerivesFrom(typeAnnotation, declaration.AttributeSchemaType);
    }

    /// <summary>
    /// Whether a node's type annotation is the declared type or derived from it. An anonymous
    /// declared type is compared under the name <see cref="AnnotationName"/> gives it, which is
    /// what a validated node of that type carries.
    /// </summary>
    private bool AnnotationDerivesFrom(XdmTypeName typeAnnotation, XmlSchemaType? declaredType)
    {
        if (declaredType is null)
            return true;
        return IsSubtypeOf(typeAnnotation, AnnotationName(declaredType));
    }

    /// <summary>
    /// Whether the element declared as <paramref name="member"/> is in the substitution group
    /// headed by <paramref name="head"/>, directly or through other members (XSD 3.3.6).
    /// </summary>
    private bool SubstitutesFor(XmlQualifiedName member, XmlQualifiedName head)
    {
        var current = member;
        for (var depth = 0; depth < 64; depth++)
        {
            if (_schemas.GlobalElements[current] is not XmlSchemaElement declaration
                || declaration.SubstitutionGroup.IsEmpty)
                return false;
            current = declaration.SubstitutionGroup;
            if (current == head)
                return true;
        }
        return false;
    }

    public bool MatchesSchemaAttribute(XdmAttribute attribute, XdmQName declarationName)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        if (_schemas.GlobalAttributes[ToXmlQualifiedName(declarationName)] is not XmlSchemaAttribute decl)
            return false;

        if (attribute.LocalName != declarationName.LocalName
            || attribute.Namespace != declarationName.Namespace)
            return false;

        if (decl.AttributeSchemaType != null)
        {
            var declaredType = ToXdmTypeName(decl.AttributeSchemaType);
            return IsSubtypeOf(attribute.TypeAnnotation, declaredType);
        }
        return true;
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.Validate
    // ──────────────────────────────────────────────

    public void ValidateXml(string xmlContent, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
        => ValidateXmlCore(xmlContent, mode, ConformanceLevel.Document, null);

    public void ValidateXmlFragment(string xmlFragment, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null,
        IReadOnlyDictionary<string, string>? inScopeNamespaces = null)
        => ValidateXmlCore(xmlFragment, mode, ConformanceLevel.Fragment, inScopeNamespaces);

    private void ValidateXmlCore(string xml, ValidationMode mode, ConformanceLevel conformance,
        IReadOnlyDictionary<string, string>? inScopeNamespaces)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemas,
            IgnoreWhitespace = false,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            ConformanceLevel = conformance,
            // Nothing the instance names is fetched: not a schema it points to with
            // xsi:schemaLocation, whatever the runtime's default resolver is.
            XmlResolver = null,
        };
        settings.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
                errors.Add(e.Message);
        };
        if (mode == ValidationMode.Lax)
            settings.ValidationFlags |= XmlSchemaValidationFlags.ProcessSchemaLocation;

        // Pre-declare any prefix→URI bindings the fragment relies on but doesn't itself
        // include (e.g. when an XSLT stylesheet declares xmlns:n on the root and the
        // synthesized element doesn't repeat it). XmlParserContext lets the reader
        // resolve those prefixes without us having to wrap the fragment in extra markup.
        XmlParserContext? parserContext = null;
        if (inScopeNamespaces is { Count: > 0 })
        {
            var nameTable = new NameTable();
            var nsManager = new XmlNamespaceManager(nameTable);
            foreach (var (prefix, uri) in inScopeNamespaces)
            {
                if (string.IsNullOrEmpty(prefix) || prefix == "xmlns") continue;
                nsManager.AddNamespace(prefix, uri);
            }
            parserContext = new XmlParserContext(nameTable, nsManager, null, XmlSpace.None);
        }

        try
        {
            using var reader = parserContext is null
                ? XmlReader.Create(new StringReader(xml), settings)
                : XmlReader.Create(new StringReader(xml), settings, parserContext);
            while (reader.Read()) { }
        }
        catch (XmlException ex)
        {
            throw new SchemaValidationException("XQDY0027",
                $"Validation failed: {ex.Message}", ex);
        }

        // Lax validation skips what has no declaration (XQuery 3.1 §3.21.2), but a node it does
        // assess, by declaration or xsi:type, must still be valid. System.Xml reports an
        // undeclared element or attribute in a namespace it has a schema for as an error, with
        // no code to tell it apart, so that one message is recognised by its wording.
        if (mode == ValidationMode.Lax)
            errors.RemoveAll(IsUndeclaredComponentError);
        if (errors.Count > 0)
        {
            throw new SchemaValidationException("XQDY0027",
                $"Validation failed: {string.Join("; ", errors)}");
        }
    }

    private static bool IsUndeclaredComponentError(string message) =>
        message.EndsWith(" is not declared.", StringComparison.Ordinal);

    /// <summary>
    /// Validates <paramref name="xmlContent"/> against the loaded schemas and returns a
    /// freshly built XDM tree whose elements/attributes carry <c>TypeAnnotation</c>
    /// values from <c>SchemaInfo.SchemaType</c>. Throws <see cref="SchemaValidationException"/>
    /// (XQDY0027) on validation failure.
    /// </summary>
    public Xdm.Nodes.XdmNode? ValidateAndAnnotate(string xmlContent, INodeBuilder builder, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
        => ValidateAndAnnotate(xmlContent, builder, mode, typeNamespaceUri, typeLocalName, documentUri: null);

    /// <summary>
    /// <see cref="ValidateAndAnnotate(string, INodeBuilder, ValidationMode, string?, string?)"/>
    /// for a document loaded from <paramref name="documentUri"/>: the annotated tree keeps it as
    /// its document and base URI, as an unvalidated load of the same file does.
    /// </summary>
    public Xdm.Nodes.XdmNode? ValidateAndAnnotate(string xmlContent, INodeBuilder builder, ValidationMode mode,
        string? typeNamespaceUri, string? typeLocalName, string? documentUri)
    {
        ArgumentNullException.ThrowIfNull(xmlContent);
        ArgumentNullException.ThrowIfNull(builder);

        // Phase 1: surface validation errors via the existing throw-on-error path.
        // XmlDocumentParser's schema overload swallows ValidationEventHandler events
        // (a parse-time tree builder shouldn't take a policy stance on schema errors),
        // so we MUST validate up front to preserve the spec contract that strict/type
        // validation raises XQDY0027 on a non-conforming document.
        ValidateXml(xmlContent, mode, typeNamespaceUri, typeLocalName);

        // Phase 2: re-parse through the schema-aware builder so SchemaInfo.SchemaType
        // is captured into XdmElement.TypeAnnotation / XdmAttribute.TypeAnnotation.
        // Each validated tree needs its own document id: with the fixed id 0 every annotated
        // document in a store was the same document to a lookup by id, so `/` from one resolved
        // to whichever was registered last (QT3's harness validates many into one store).
        var docId = builder.AllocateDocumentId();
        var startNodeId = builder.AllocateId();
        var parser = new Xdm.Parsing.XmlDocumentParser(
            docId, startNodeId, builder.InternNamespace, preserveWhitespace: true);
        // Name every type annotation here, not from the store. The store's namespace ids are its
        // own, but element(*, T) and IsSubtypeOf identify a type's namespace by TypeNamespaceId,
        // so an annotation the store named never equalled the type a query named. An anonymous
        // type has no name at all and gets one of ours.
        var recipes = Execution.TypeCastHelper.TypedValueRecipes.GetOrCreateValue(builder);
        parser.SchemaTypeAnnotator = type =>
        {
            if (type.QualifiedName.Namespace == XmlSchema.Namespace && !type.QualifiedName.IsEmpty)
                return null;
            var name = AnnotationName(type);
            if (!recipes.ContainsKey(name) && TypedValueRecipe(type) is { } recipe)
                recipes[name] = recipe;
            return name;
        };

        Xdm.Parsing.ParseResult result;
        try
        {
            using var reader = new System.IO.StringReader(xmlContent);
            result = parser.Parse(reader, documentUri: documentUri, _schemas);
        }
        catch (System.Xml.XmlException ex)
        {
            // Unlikely after the validation pass above succeeded, but stay defensive.
            throw new SchemaValidationException("XQDY0027",
                $"Validation succeeded but annotating parse failed: {ex.Message}", ex);
        }

        // The parser numbered the tree's nodes from startNodeId on; reserve that range, or the
        // next allocation reuses it and a second validated document overwrites this one's nodes.
        var lastId = startNodeId;
        foreach (var node in result.Nodes)
        {
            var annotation = node switch
            {
                XdmElement e => e.TypeAnnotation,
                XdmAttribute a => a.TypeAnnotation,
                _ => XdmTypeName.Untyped,
            };
            if (annotation.Namespace != NamespaceId.Xsd || annotation.LocalName is "QName" or "NOTATION")
                Execution.TypeCastHelper.AnnotatingStores.AddOrUpdate(node, builder);
            builder.RegisterNode(node);
            if (node.Id.Value > lastId.Value) lastId = node.Id;
        }
        builder.ReserveIdsThrough(lastId);
        return result.Document;
    }

    /// <summary>The namespace of the names this provider gives anonymous types.</summary>
    private const string AnonymousTypeNamespace = "http://phoenixml.dev/xquery/anonymous-types";

    private readonly Dictionary<XmlSchemaType, XdmTypeName> _anonymousTypeNames = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, XmlSchemaType> _anonymousTypesByName = new(StringComparer.Ordinal);

    /// <summary>
    /// The name a node of this type is annotated with: the type's own, in the id scheme the rest
    /// of the engine uses for type names, or — for an anonymous type, which XDM 3.1 §2.7.1 says
    /// gets an implementation-defined name — one made up here and resolvable by
    /// <see cref="FindSchemaType"/>.
    /// </summary>
    private XdmTypeName AnnotationName(XmlSchemaType type)
    {
        if (!type.QualifiedName.IsEmpty)
            return ToXdmTypeName(type);
        lock (_anonymousTypeNames)
        {
            if (!_anonymousTypeNames.TryGetValue(type, out var name))
            {
                RememberNamespaceId(AnonymousTypeNamespace);
                name = new XdmTypeName(TypeNamespaceId(AnonymousTypeNamespace),
                    "anonymous-type-" + (_anonymousTypeNames.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                _anonymousTypeNames[type] = name;
                _anonymousTypesByName[name.LocalName] = type;
            }
            return name;
        }
    }

    /// <summary>
    /// How a node of this type atomizes, or null when its typed value stays xs:untypedAtomic
    /// here: mixed and empty content, unions (the annotation normally names the member that
    /// matched instead), and types derived from xs:QName or xs:NOTATION.
    /// </summary>
    private static Func<string, object?>? TypedValueRecipe(XmlSchemaType type)
    {
        if (type is XmlSchemaComplexType complex)
        {
            // Element-only content has no typed value: atomizing such a node is FOTY0012.
            if (complex.ContentType == XmlSchemaContentType.ElementOnly)
                return _ => throw new Execution.XQueryRuntimeException("FOTY0012",
                    "A node whose type has element-only content has no typed value.");
            if (complex.ContentType != XmlSchemaContentType.TextOnly)
                return null;
            // Simple content: the typed value is that of the simple type it extends or restricts.
            XmlSchemaType? t = complex;
            while (t is XmlSchemaComplexType)
                t = t.BaseXmlSchemaType;
            return t is XmlSchemaSimpleType contentType ? TypedValueRecipe(contentType) : null;
        }
        if (type is not XmlSchemaSimpleType simple || simple.Datatype is not { } datatype)
            return null;
        switch (datatype.Variety)
        {
            case XmlSchemaDatatypeVariety.Atomic:
                var builtIn = BuiltInBaseName(simple);
                if (builtIn is "QName" or "NOTATION")
                    return Execution.TypeCastHelper.NamespaceSensitiveRecipe;
                if (builtIn is "anySimpleType" or "anyAtomicType")
                    return null;
                // A named type's value is an instance of it: the nearest named type, for an
                // anonymous restriction of one.
                XmlSchemaType? named = simple;
                while (named is { QualifiedName.IsEmpty: true })
                    named = named.BaseXmlSchemaType;
                if (named is null || named.QualifiedName.Namespace == XmlSchema.Namespace)
                    return value => Execution.TypeCastHelper.BuiltInTypedValue(builtIn, value)
                        ?? new Xdm.XsUntypedAtomic(value);
                var typeNamespace = named.QualifiedName.Namespace;
                var typeName = named.QualifiedName.Name;
                return value => Execution.TypeCastHelper.BuiltInTypedValue(builtIn, value) is { } typed
                    ? Execution.TypeCastHelper.WithSchemaType(typed, typeNamespace, typeName)
                    : new Xdm.XsUntypedAtomic(value);
            case XmlSchemaDatatypeVariety.List:
                var listType = simple;
                while (listType.Content is XmlSchemaSimpleTypeRestriction && listType.BaseXmlSchemaType is XmlSchemaSimpleType baseList)
                    listType = baseList;
                if (listType.Content is not XmlSchemaSimpleTypeList { BaseItemType: { } itemType }
                    || TypedValueRecipe(itemType) is not { } itemRecipe)
                    return null;
                return value =>
                {
                    var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    var items = new List<object?>(tokens.Length);
                    foreach (var token in tokens)
                    {
                        if (itemRecipe(token) is object?[] several) items.AddRange(several);
                        else items.Add(itemRecipe(token));
                    }
                    return items.ToArray();
                };
            default:
                return null;
        }
    }

    private static string BuiltInBaseName(XmlSchemaSimpleType type)
    {
        for (XmlSchemaType? t = type; t != null; t = t.BaseXmlSchemaType)
            if (t.QualifiedName.Namespace == XmlSchema.Namespace && !t.QualifiedName.IsEmpty)
                return t.QualifiedName.Name;
        return "anyAtomicType";
    }

    /// <summary>
    /// Validates <paramref name="node"/> with its markup, serialized through
    /// <paramref name="nodeProvider"/> (#40). Returns the node unchanged.
    /// </summary>
    public XdmNode Validate(XdmNode node, INodeProvider nodeProvider, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(nodeProvider);
        ValidateXml(Functions.SerializeFunction.SerializeNodeToXml(node, nodeProvider), mode, typeNamespaceUri, typeLocalName);
        return node;
    }

    public XdmNode Validate(XdmNode node, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        // Without a node provider this method cannot reach a node's children or attributes, and it
        // used to validate the element's start tag wrapped around its string value — dropping all
        // structure, so valid instances failed and invalid ones passed (#40). Refuse instead of
        // answering a different question; the overload taking an INodeProvider serializes the node.
        if (node is XdmElement { Children.Count: > 0 } or XdmElement { Attributes.Count: > 0 } or XdmDocument { Children.Count: > 0 })
            throw new InvalidOperationException(
                "Validating a node with children or attributes needs the node provider that resolves them: " +
                "call Validate(node, nodeProvider, mode, ...). Without it only the node's text could be validated, " +
                "which is not the node.");
        // Phase 1: Validate the XML against schemas.
        // We serialize the XDM node to XML, run it through a validating XmlReader,
        // and collect any errors. If strict or type mode and errors occur, throw.
        //
        // Phase 2 (future): Return a deep copy with type annotations applied.
        // For now, we return the original node after validation passes —
        // full copy-with-annotations requires deeper node store integration.

        // Not the public getter: under StrictStringValue it throws for a node with neither a cached
        // value nor a resolver, and a childless node's string value is known to be "" (#37).
        var xml = Execution.QueryExecutionContext.CachedOrResolvableStringValue(node)
                  ?? (node is XdmElement { Children.Count: 0 } or XdmDocument { Children.Count: 0 } ? "" : node.StringValue);

        // For elements, we need to reconstruct the XML with proper markup
        if (node is XdmElement elem)
        {
            var ns = GetNamespaceUri(elem.Namespace);
            using var sw = new StringWriter();
            using (var xw = XmlWriter.Create(sw, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                xw.WriteStartElement(elem.Prefix ?? "", elem.LocalName, ns);
                xw.WriteString(xml);
                xw.WriteEndElement();
            }
            xml = sw.ToString();
        }
        else if (node is XdmDocument)
        {
            // For document nodes, StringValue gives us the text content.
            // Full document serialization requires node store traversal.
            // For now, wrap in a minimal document if we don't have markup.
            if (!xml.TrimStart().StartsWith('<'))
                xml = $"<root>{xml}</root>";
        }

        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemas,
            IgnoreWhitespace = false,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            ConformanceLevel = ConformanceLevel.Fragment,
            XmlResolver = null,
        };

        settings.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
                errors.Add(e.Message);
        };

        if (mode == ValidationMode.Lax)
            settings.ValidationFlags |= XmlSchemaValidationFlags.ProcessSchemaLocation;

        // Run the validating reader
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read()) { }
        }
        catch (XmlException ex)
        {
            throw new SchemaValidationException("XQDY0027",
                $"Validation failed: {ex.Message}", ex);
        }

        if (mode != ValidationMode.Lax && errors.Count > 0)
        {
            throw new SchemaValidationException("XQDY0027",
                $"Validation failed: {string.Join("; ", errors)}");
        }

        // For type mode, verify the type constraint
        if (mode == ValidationMode.Type && typeLocalName != null)
        {
            var expectedNs = !string.IsNullOrEmpty(typeNamespaceUri)
                ? typeNamespaceUri
                : "http://www.w3.org/2001/XMLSchema";
            var expectedType = FindSchemaTypeByUri(expectedNs, typeLocalName);
            if (expectedType == null)
            {
                throw new SchemaValidationException("XQDY0027",
                    $"Unknown type: {typeLocalName}");
            }
        }

        // Return original node — full copy-with-annotations is a Phase 2 feature
        return node;
    }

    // ──────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────

    private bool HasNamespace(string targetNamespace)
    {
        foreach (XmlSchema _ in _schemas.Schemas(targetNamespace ?? ""))
            return true;
        return false;
    }

    private IEnumerable<string> EnumerateLoadedNamespaces()
    {
        foreach (XmlSchema schema in _schemas.Schemas())
            yield return schema.TargetNamespace ?? "";
    }

    private XmlSchemaType? FindSchemaType(XdmTypeName typeName)
    {
        var ns = GetNamespaceUri(typeName.Namespace);
        return FindSchemaTypeByUri(ns, typeName.LocalName);
    }

    private XmlSchemaType? FindSchemaTypeByUri(string ns, string localName)
    {
        if (ns == AnonymousTypeNamespace)
        {
            lock (_anonymousTypeNames)
                return _anonymousTypesByName.GetValueOrDefault(localName);
        }
        var qn = new XmlQualifiedName(localName, ns);
        if (_schemas.GlobalTypes[qn] is XmlSchemaType t)
            return t;
        return XmlSchemaType.GetBuiltInSimpleType(qn)
            ?? (XmlSchemaType?)XmlSchemaType.GetBuiltInComplexType(qn);
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.TryCastToSchemaSimpleType
    // ──────────────────────────────────────────────

    /// <summary>
    /// Validates a lexical value against a schema-defined simple type, for cast/castable.
    ///
    /// The facet checking is XmlSchemaDatatype.ParseValue's, not ours: it already enforces
    /// pattern, enumeration, length, bounds and whitespace for every XSD simple type,
    /// including unions and lists. Reimplementing that in the engine would be both large and
    /// worse.
    /// </summary>
    public IEnumerable<string> GetSchemaSimpleTypeNames(string? namespaceUri)
    {
        var ns = namespaceUri ?? "";
        foreach (XmlSchemaType type in _schemas.GlobalTypes.Values)
            if (type is XmlSchemaSimpleType && type.QualifiedName.Namespace == ns)
                yield return type.QualifiedName.Name;
    }

    /// <inheritdoc />
    public bool HasIdrefTypedValue(XdmTypeName typeAnnotation, string stringValue)
    {
        ArgumentNullException.ThrowIfNull(stringValue);
        var type = FindSchemaType(typeAnnotation);
        // Simple content: the typed value is that of the simple type it extends or restricts.
        while (type is XmlSchemaComplexType complex)
        {
            if (complex.ContentType != XmlSchemaContentType.TextOnly)
                return false;
            type = complex.BaseXmlSchemaType;
        }
        return type is XmlSchemaSimpleType simple && ContainsIdref(simple, stringValue, depth: 0);
    }

    private static bool ContainsIdref(XmlSchemaSimpleType type, string value, int depth)
    {
        if (type.Datatype is not { } datatype || depth > 16)
            return false;
        // xs:IDREF, xs:IDREFS and what is derived from them by restriction.
        if (datatype.TypeCode == XmlTypeCode.Idref)
            return !string.IsNullOrWhiteSpace(value);
        var declared = type;
        while (declared.Content is XmlSchemaSimpleTypeRestriction && declared.BaseXmlSchemaType is XmlSchemaSimpleType baseType)
            declared = baseType;
        switch (declared.Content)
        {
            case XmlSchemaSimpleTypeList { BaseItemType: { } itemType }:
                foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (ContainsIdref(itemType, token, depth + 1))
                        return true;
                return false;
            // A union's value has the type of the first member that accepts it.
            case XmlSchemaSimpleTypeUnion { BaseMemberTypes: { } members }:
                foreach (var member in members)
                {
                    if (member.Datatype is not { } memberDatatype)
                        continue;
                    try
                    {
                        memberDatatype.ParseValue(value.Trim(), new NameTable(), new PrefixResolver(null));
                    }
                    catch (Exception ex) when (ex is XmlSchemaException or FormatException or OverflowException or ArgumentException)
                    {
                        continue;
                    }
                    return ContainsIdref(member, value, depth + 1);
                }
                return false;
            default:
                return false;
        }
    }

    public bool IsSchemaSimpleTypeDerivedFrom(string? namespaceUri, string localName, string? baseNamespaceUri, string baseLocalName)
    {
        if (FindSchemaTypeByUri(namespaceUri ?? "", localName) is not XmlSchemaSimpleType type)
            return false;
        for (var t = type.BaseXmlSchemaType; t != null; t = t.BaseXmlSchemaType)
        {
            if (t.QualifiedName.Name == baseLocalName && t.QualifiedName.Namespace == (baseNamespaceUri ?? ""))
                return true;
        }
        return false;
    }

    public object?[]? GetSchemaListItems(string? namespaceUri, string localName, string lexicalValue)
    {
        if (FindSchemaTypeByUri(namespaceUri ?? "", localName) is not XmlSchemaSimpleType { Datatype.Variety: XmlSchemaDatatypeVariety.List } list
            || TypedValueRecipe(list) is not { } recipe)
            return null;
        return recipe(lexicalValue) as object?[];
    }

    public SchemaSimpleType? GetSchemaSimpleType(string? namespaceUri, string localName)
    {
        if (FindSchemaTypeByUri(namespaceUri ?? "", localName) is not XmlSchemaSimpleType simple
            || simple.Datatype is not { } datatype)
            return null;
        var variety = datatype.Variety switch
        {
            XmlSchemaDatatypeVariety.List => SchemaSimpleTypeVariety.List,
            XmlSchemaDatatypeVariety.Union => SchemaSimpleTypeVariety.Union,
            _ => SchemaSimpleTypeVariety.Atomic,
        };
        var members = new List<SchemaTypeReference>();
        var isPureUnion = false;
        if (variety == SchemaSimpleTypeVariety.Union)
        {
            isPureUnion = IsPureUnion(simple);
            // A union derived by restriction keeps its base union's members.
            var unionType = simple;
            while (unionType.Content is XmlSchemaSimpleTypeRestriction && unionType.BaseXmlSchemaType is XmlSchemaSimpleType baseSimple)
                unionType = baseSimple;
            if (unionType.Content is XmlSchemaSimpleTypeUnion union && union.BaseMemberTypes is { } memberTypes)
            {
                foreach (var member in memberTypes)
                {
                    var name = member.QualifiedName;
                    var isBuiltIn = name.Namespace == XmlSchema.Namespace;
                    // An anonymous member has no name to refer to; describe it by its nearest
                    // built-in, which is what membership can be decided against.
                    if (name.IsEmpty)
                        members.Add(new SchemaTypeReference(XmlSchema.Namespace, BuiltInNameOf(member), IsBuiltIn: true));
                    else
                        members.Add(new SchemaTypeReference(name.Namespace, name.Name, isBuiltIn));
                }
            }
        }
        return new SchemaSimpleType(namespaceUri, localName, variety, members)
        {
            BuiltInBaseLocalName = variety == SchemaSimpleTypeVariety.Atomic ? BuiltInNameOf(simple) : null,
            IsPureUnion = isPureUnion,
            IsDerivedByRestriction = simple.Content is XmlSchemaSimpleTypeRestriction,
        };

        static bool IsPureUnion(XmlSchemaSimpleType type) =>
            type.Content is XmlSchemaSimpleTypeUnion { BaseMemberTypes: { } memberTypes }
            && memberTypes.All(m => m.Datatype?.Variety switch
            {
                XmlSchemaDatatypeVariety.Atomic => true,
                XmlSchemaDatatypeVariety.Union => IsPureUnion(m),
                _ => false,
            });

        static string BuiltInNameOf(XmlSchemaSimpleType type)
        {
            for (XmlSchemaType? t = type; t != null; t = t.BaseXmlSchemaType)
                if (t.QualifiedName.Namespace == XmlSchema.Namespace && !t.QualifiedName.IsEmpty)
                    return t.QualifiedName.Name;
            return "anyAtomicType";
        }
    }

    public bool TryCastToSchemaSimpleType(string? namespaceUri, string localName, string lexicalValue)
        => TryCastToSchemaSimpleType(namespaceUri, localName, lexicalValue, resolvePrefix: null);

    public bool TryCastToSchemaSimpleType(string? namespaceUri, string localName, string lexicalValue, Func<string, string?>? resolvePrefix)
    {
        var type = FindSchemaTypeByUri(namespaceUri ?? "", localName);

        // "No such type" and "not a simple type" are STATIC errors about the query, so they
        // throw. Only "the value does not satisfy the type" is a false — that is the ordinary
        // castable outcome and must not be reported as a broken query.
        if (type is null)
            throw new SchemaException("XPST0051",
                $"'{{{namespaceUri}}}{localName}' is not a type declared by any imported schema.");
        if (type is not XmlSchemaSimpleType simple)
            throw new SchemaException("XPST0051",
                $"'{{{namespaceUri}}}{localName}' is a complex type; only simple types can be a cast target.");
        if (simple.Datatype is not { } datatype)
            throw new SchemaException("XPST0051",
                $"'{{{namespaceUri}}}{localName}' has no usable value space.");

        try
        {
            // A QName-based type parses its prefix through the resolver; with none, System.Xml
            // dereferenced null (QT3 qname-cast-*, CastAs-UnionType-10..33). An unbound prefix
            // is then an ordinary "not a value of this type".
            datatype.ParseValue(lexicalValue, new NameTable(), new PrefixResolver(resolvePrefix));
            return true;
        }
        catch (XmlSchemaException) { return false; }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private bool IsInSubstitutionGroup(XdmElement element, XmlSchemaElement headDecl)
    {
        foreach (XmlSchemaElement globalElem in _schemas.GlobalElements.Values)
        {
            if (globalElem.SubstitutionGroup == headDecl.QualifiedName
                && globalElem.QualifiedName.Name == element.LocalName)
            {
                var elemNs = GetNamespaceUri(element.Namespace);
                if (globalElem.QualifiedName.Namespace == elemNs)
                    return true;
            }
        }
        return false;
    }

    private XdmTypeName ToXdmTypeName(XmlSchemaType schemaType)
    {
        var qn = schemaType.QualifiedName;
        if (qn == null || string.IsNullOrEmpty(qn.Name))
            return XdmTypeName.AnyType;

        var ns = TypeNamespaceId(qn.Namespace);

        // Make sure the URI is round-trippable from the synthesized NamespaceId.
        RememberNamespaceId(qn.Namespace);

        return new XdmTypeName(ns, qn.Name);
    }

    private XmlQualifiedName ToXmlQualifiedName(XdmQName name)
    {
        var ns = GetNamespaceUri(name.Namespace);
        return new XmlQualifiedName(name.LocalName, ns);
    }

    private string GetNamespaceUri(NamespaceId nsId)
        => _namespaceUriById.TryGetValue(nsId, out var uri) ? uri : "";

    /// <summary>
    /// Records the (NamespaceId → URI) mapping for a URI a caller has just registered with
    /// the provider. The id is computed using the same hash scheme that <c>SchemaFeatureChecker</c>
    /// uses, so subsequent lookups against XdmQNames built by that checker round-trip correctly.
    /// Built-in XSD/XML/XSI URIs are pre-registered and not re-hashed.
    /// </summary>
    private void RememberNamespaceId(string namespaceUri)
    {
        if (string.IsNullOrEmpty(namespaceUri)) return;
        if (namespaceUri is "http://www.w3.org/2001/XMLSchema"
            or "http://www.w3.org/XML/1998/namespace"
            or "http://www.w3.org/2001/XMLSchema-instance")
            return;
        var id = new NamespaceId((uint)namespaceUri.GetHashCode(StringComparison.Ordinal));
        _namespaceUriById[id] = namespaceUri;
    }
}
