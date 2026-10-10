using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Performs static analysis on XQuery expressions.
/// Includes namespace resolution, variable binding, function resolution, and type inference.
/// </summary>
public sealed class StaticAnalyzer
{
    private readonly StaticContext _context;
    /// <summary>
    /// Cycle detection for module loads. Keyed on the resolved file path so that
    /// multiple module files sharing the same target namespace (e.g. impl1.xqm
    /// and impl2.xqm both <c>module namespace impl = "..."</c>) each get loaded
    /// once and merged. Per XQuery 3.1 §4.12.2, all public declarations across
    /// all files of namespace M must be accessible to importers of M, not just
    /// those from the <c>at</c> hint the importer used.
    /// </summary>
    private readonly HashSet<string> _resolvedModuleFiles = new(StringComparer.Ordinal);

    /// <summary>
    /// For a module file, the URI its import named: where the module is, as the query knows it.
    /// The file read can be somewhere else (a host mapping, the cache of an HTTP fetch, the
    /// canonical path a policy authorised), and that is not the module's base URI.
    /// </summary>
    private readonly Dictionary<string, string> _moduleLocations = new(StringComparer.Ordinal);

    /// <summary>
    /// The absolute URI a location hint names: the hint itself, or the hint resolved against
    /// the static base URI. Null when neither is absolute; the working directory is not used.
    /// </summary>
    private string? LocationOf(string hint)
        => Uri.TryCreate(hint, UriKind.Absolute, out var absolute) && absolute.Scheme.Length > 1
            ? absolute.AbsoluteUri
            : ResolveAgainstBase(hint);

    /// <summary>
    /// While a library module's own imports are resolved: where that module is. Null value
    /// with <see cref="_inLibraryModule"/> set means its location is not known.
    /// </summary>
    private string? _libraryModuleLocation;
    private bool _inLibraryModule;

    /// <summary>The module whose import is being resolved, for the host's resolver.</summary>
    private Uri? ImportingModule()
    {
        var location = _inLibraryModule ? _libraryModuleLocation : _context.BaseUri;
        return location != null && Uri.TryCreate(location, UriKind.Absolute, out var uri) ? uri : null;
    }

    private void RememberLocation(string? modulePath, string hint)
    {
        if (modulePath != null && LocationOf(hint) is { } location)
            _moduleLocations.TryAdd(modulePath, location);
    }

    public StaticAnalyzer(StaticContext? context = null)
    {
        _context = context ?? StaticContext.Default;
    }

    /// <summary>
    /// Analyzes an expression and returns the result with any errors.
    /// </summary>
    public AnalysisResult Analyze(XQueryExpression expression)
    {
        var errors = new List<AnalysisError>();

        // Phase 0: Pre-register prolog declarations so they're visible during analysis
        PreRegisterDeclarations(expression, errors);

        // Phase 0b: Inject imported module function/variable declarations into the main module AST
        // so the optimizer creates physical operators for them (replacing DeclaredFunctionPlaceholder at runtime)
        expression = InjectImportedDeclarations(expression);

        // Phase 1: Namespace resolution
        var nsResolver = new NamespaceResolver(_context.Namespaces);
        expression = nsResolver.Resolve(expression, errors);

        // Phase 1b: Validate schema-element/attribute references against the registered
        // ISchemaProvider — the spec requires XPST0008 when a schema-element(Name) refers
        // to a declaration not in the in-scope schema definitions. When no provider is
        // registered (rare opt-out), all such references trivially fail.
        if (_context.SchemaProvider is { } schemaProvider)
        {
            var schemaChecker = new SchemaFeatureChecker(schemaProvider);
            schemaChecker.Walk(expression);
            errors.AddRange(schemaChecker.Errors);
            if (expression is ModuleExpression { SchemaTypedSequenceTypes.Count: > 0 } module)
                errors.AddRange(SchemaFeatureChecker.CheckSchemaItemTypes(module.SchemaTypedSequenceTypes, schemaProvider));
            if (expression is ModuleExpression { SchemaKindTestTypes.Count: > 0 } kindModule)
                foreach (var (ns, local, kind) in kindModule.SchemaKindTestTypes)
                    if (!schemaProvider.HasSchemaType(ns, local))
                        errors.Add(new AnalysisError("XPST0008",
                            $"Unknown schema type 'Q{{{ns}}}{local}' in {kind}() test: no imported schema declares it", null));
        }
        else
        {
            // No provider — every schema-element/attribute reference is necessarily
            // unresolvable. Walk the AST and emit XPST0008 for any such references.
            var noProvider = new NullSchemaProvider();
            var schemaChecker = new SchemaFeatureChecker(noProvider);
            schemaChecker.Walk(expression);
            errors.AddRange(schemaChecker.Errors);
        }

        // Phase 2: Variable binding
        var varBinder = new VariableBinder(_context);
        expression = varBinder.Bind(expression, errors);

        // Phase 3: Function resolution
        var funcResolver = new FunctionResolver(_context.Functions, _context.Namespaces,
            _context.ImportedModules.Keys);
        expression = funcResolver.Resolve(expression, errors);

        // Phase 4: Type inference
        var typeInferrer = new TypeInferrer(_context);
        typeInferrer.Infer(expression, errors);

        return new AnalysisResult(expression, errors);
    }

    /// <summary>
    /// Attempts to resolve and load a library module from its location hints.
    /// Returns true if the module was loaded successfully, false if it couldn't be found.
    /// </summary>
    private bool TryResolveModule(ModuleImportExpression modImport, List<AnalysisError> errors)
    {
        // Per XQuery 3.1 §4.12.2: when a namespace M is imported, ALL public
        // declarations across ALL module files in M's context must be accessible —
        // not just those from the importer's `at` hint. Collect every candidate
        // file (location hints + ExternalModules registry for the namespace)
        // and load each one not yet seen. Per-file cycle detection prevents
        // re-loading on transitive imports.
        var resolvedPaths = new List<string>();
        // Modules whose text the host's resource resolver supplied, with the URI each is known by.
        var suppliedModules = new List<(string BaseUri, string Source)>();

        // First, try resolving each location hint
        foreach (var hint in modImport.LocationHints)
        {
            string? modulePath = null;

            // Check location hint mapping (e.g. http:// URI → actual file path), by the hint as
            // written and as resolved against the base URI. A relative hint ("lib2.xqm" under an
            // http:// base) was looked up only as written, so a host that mapped its absolute
            // location saw the module fetched over the network instead (QT3 d1e78807j: a 404).
            if (_context.ExternalModuleLocations != null
                && (_context.ExternalModuleLocations.TryGetValue(hint, out var mappedPath)
                    || (ResolveAgainstBase(hint) is { } absoluteHint
                        && _context.ExternalModuleLocations.TryGetValue(absoluteHint, out mappedPath))))
            {
                modulePath = mappedPath;
            }
            else if (_context.ResourcePolicy is { } policy)
            {
                // The host's own content first: a module it supplies is compiled from that text,
                // known by the base URI the host gives it, and nothing is opened here.
                Security.ResourceContent? supplied;
                try
                {
                    supplied = Security.ResourceGate.HostContent(policy, hint,
                        new Security.ResourceCaller(
                            _context.BaseUri != null && Uri.TryCreate(_context.BaseUri, UriKind.Absolute, out var hintBase) ? hintBase : null,
                            ImportingModule()),
                        Security.ResourceAccessKind.ImportStylesheet);
                }
                catch (Security.ResourceAccessDeniedException e)
                {
                    errors.Add(new AnalysisError(XQueryErrorCodes.XQST0059, e.Message, modImport.Location));
                    continue;
                }
                if (supplied != null)
                {
                    suppliedModules.Add((supplied.BaseUri.AbsoluteUri, supplied.ReadText()));
                    continue;
                }
                // Under a resource policy the hint is resolved, authorised for import, and only
                // then read — from the URI the policy authorised. (Host-configured locations,
                // above and in ExternalModules, are the host's own choice and stay trusted.)
                if (AuthorizeImport(policy, hint, errors, modImport) is not { } authorized)
                    continue;
                if (authorized.IsFile)
                    modulePath = authorized.LocalPath;
                else if (authorized.Scheme == Uri.UriSchemeHttp || authorized.Scheme == Uri.UriSchemeHttps)
                    modulePath = DownloadHttpModuleToTempFile(authorized, errors, modImport, policy);
                RememberLocation(modulePath, hint);
                if (modulePath != null && System.IO.File.Exists(modulePath))
                    resolvedPaths.Add(modulePath);
                continue;
            }
            else if (Uri.TryCreate(hint, UriKind.Absolute, out var absUri) && absUri.IsFile)
            {
                modulePath = absUri.LocalPath;
            }
            else if (Uri.TryCreate(hint, UriKind.Absolute, out var absHttpUri)
                && (absHttpUri.Scheme == Uri.UriSchemeHttp || absHttpUri.Scheme == Uri.UriSchemeHttps))
            {
                // HTTP(S) module location — fetch and cache to a temp file so the existing
                // file-based TryLoadModuleFile path can consume it (Martin Honnen 2026-05-18,
                // fn:load-xquery-module('uri', map{'location-hints':'https://...xq'}).
                var tempPath = DownloadHttpModuleToTempFile(absHttpUri, errors, modImport);
                if (tempPath != null) modulePath = tempPath;
            }
            else if (_context.BaseUri != null)
            {
                if (Uri.TryCreate(_context.BaseUri, UriKind.Absolute, out var baseUri))
                {
                    if (Uri.TryCreate(baseUri, hint, out var resolved))
                    {
                        if (resolved.IsFile)
                            modulePath = resolved.LocalPath;
                        else if (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps)
                        {
                            var tempPath = DownloadHttpModuleToTempFile(resolved, errors, modImport);
                            if (tempPath != null) modulePath = tempPath;
                        }
                    }
                }
            }

            modulePath ??= hint;
            if (TryFindFile(modulePath, out var foundPath))
            {
                RememberLocation(foundPath, hint);
                resolvedPaths.Add(foundPath);
            }
        }

        // Always also pull in every file registered for this namespace via the
        // ExternalModules catalog (test environment / host-supplied module map).
        // Multi-location module aggregation: impl1.xqm + impl2.xqm both under
        // namespace `impl` must be loaded together so impl:f1#1 (from impl1)
        // AND impl:f1#2/$impl:v1 (from impl2) are visible to importers of impl,
        // regardless which `at` hint the importer used.
        if (_context.ExternalModules != null
            && _context.ExternalModules.TryGetValue(modImport.NamespaceUri, out var registeredPaths))
        {
            foreach (var p in registeredPaths)
            {
                if (TryFindFile(p, out var found) && !resolvedPaths.Contains(found))
                    resolvedPaths.Add(found);
            }
        }

        if (resolvedPaths.Count == 0 && suppliedModules.Count == 0)
            return false;

        bool anyLoaded = false;
        foreach (var (moduleBaseUri, moduleText) in suppliedModules)
        {
            if (!_resolvedModuleFiles.Add(moduleBaseUri))
            {
                anyLoaded = true; // already loaded earlier; treat as success
                continue;
            }
            if (TryLoadModuleFile(moduleBaseUri, modImport, errors, moduleText))
                anyLoaded = true;
        }
        foreach (var modulePath in resolvedPaths)
        {
            // Per-file cycle detection — multiple modules can share a namespace,
            // so we can't key on namespace alone (that would skip impl2.xqm after
            // impl1.xqm was loaded for the same namespace).
            var canonical = TryFindFile(modulePath, out var found2) ? found2 : modulePath;
            if (!_resolvedModuleFiles.Add(canonical))
            {
                anyLoaded = true; // already loaded earlier; treat as success
                continue;
            }
            if (TryLoadModuleFile(modulePath, modImport, errors))
                anyLoaded = true;
        }

        return anyLoaded;
    }

    /// <summary>
    /// Loads the schema an <c>import schema</c> names into the provider and registers what the
    /// import brings into scope. Shared by the main module and library modules: a library
    /// module's import was never loaded, so <c>validate</c> in its functions ran against no
    /// schema and accepted anything.
    /// </summary>
    private void LoadSchemaImport(SchemaImportExpression schemaImport, List<AnalysisError> errors)
    {
        // Route the import through the registered ISchemaProvider so it can
        // load the schema (or surface XQST0059 if the location can't be found).
        // With no provider registered (rare opt-out), schema imports raise
        // XQST0009 to match the "schema not supported" semantic.
        if (_context.SchemaProvider is null)
        {
            errors.Add(new AnalysisError(
                "XQST0009",
                $"import schema is not supported because schemaProvider was set to null on the QueryEngine (target namespace '{schemaImport.TargetNamespace}')",
                schemaImport.Location));
            return;
        }
        try
        {
            // Resolve relative location hints against the query's base URI before
            // handing them to the schema provider — mirrors what module imports do
            // a few cases above. Without this, `import schema '' at 'schema1.xsd'`
            // resolves the hint against the application's CWD instead of the
            // location the query was loaded from, breaking any embedded host that
            // ships query files alongside their schemas.
            var resolvedHints = ResolveLocationHints(schemaImport.LocationHints);
            if (_context.ResourcePolicy is { } schemaPolicy && resolvedHints is { Count: > 0 })
            {
                // Authorise each hint for import; a refused one is never handed on.
                var allowed = new List<string>();
                var schemaBase = _context.BaseUri != null && Uri.TryCreate(_context.BaseUri, UriKind.Absolute, out var sb) ? sb : null;
                foreach (var hint in resolvedHints)
                {
                    // With a host resource resolver the location is only made absolute here: the
                    // resolver is asked for its content when the schema set fetches it, and the
                    // policy's rules are applied there to whatever the host does not supply.
                    if (schemaPolicy.ResourceResolver != null
                        && Security.ResourcePolicy.Resolve(hint, schemaBase) is { } located)
                        allowed.Add(located.AbsoluteUri);
                    else if (AuthorizeImport(schemaPolicy, hint, errors, schemaImport) is { } ok)
                        allowed.Add(ok.AbsoluteUri);
                }
                if (allowed.Count == 0)
                    return;
                resolvedHints = allowed;
            }
            _context.SchemaProvider.ImportSchema(
                schemaImport.TargetNamespace,
                resolvedHints,
                _context.ResourcePolicy,
                ImportingModule());
            // Each imported simple type has a constructor function of its name.
            var typeNs = schemaImport.TargetNamespace;
            var typeNsId = _context.Namespaces.GetOrCreateId(typeNs);
            foreach (var typeName in _context.SchemaProvider.GetSchemaSimpleTypeNames(typeNs))
            {
                var ctorName = new QName(typeNsId, typeName) { RuntimeNamespace = typeNs };
                if (_context.Functions.Resolve(ctorName, 1) is null)
                    _context.Functions.Register(new SchemaTypeConstructorFunction(ctorName, typeNs, typeName));
            }
            // Register the prefix binding if one was given so subsequent expressions
            // can resolve names in the imported namespace.
            if (!string.IsNullOrEmpty(schemaImport.Prefix))
                _context.Namespaces.RegisterNamespace(schemaImport.Prefix, schemaImport.TargetNamespace);
        }
        catch (SchemaException ex)
        {
            errors.Add(new AnalysisError(ex.ErrorCode, ex.Message, schemaImport.Location));
        }
    }

    /// <summary>
    /// Resolves each location hint against the query's base URI when the hint is relative,
    /// returning a list with the resolved file paths (or the original hint as a fallback).
    /// Mirrors the resolution module imports do in <see cref="TryResolveModule"/>.
    /// </summary>
    private IReadOnlyList<string>? ResolveLocationHints(IReadOnlyList<string>? hints)
    {
        if (hints is null || hints.Count == 0)
            return hints;

        var resolved = new List<string>(hints.Count);
        foreach (var hint in hints)
        {
            if (string.IsNullOrEmpty(hint))
                continue;

            // Already-absolute file URIs and rooted paths are taken as-is.
            if (Uri.TryCreate(hint, UriKind.Absolute, out var absUri) && absUri.IsFile)
            {
                resolved.Add(absUri.LocalPath);
                continue;
            }
            if (System.IO.Path.IsPathRooted(hint))
            {
                resolved.Add(hint);
                continue;
            }

            // Relative hint — resolve against the query's base URI when one is set.
            if (_context.BaseUri != null
                && Uri.TryCreate(_context.BaseUri, UriKind.Absolute, out var baseUri)
                && Uri.TryCreate(baseUri, hint, out var resolvedUri)
                && resolvedUri.IsFile)
            {
                resolved.Add(resolvedUri.LocalPath);
            }
            else
            {
                // No base URI or non-file scheme — fall back to the raw hint and let the
                // schema provider try its own resolution (CWD, custom resolvers, etc.).
                resolved.Add(hint);
            }
        }
        return resolved;
    }

    /// <summary>
    /// Tries to find a file at the given path or its full-path equivalent.
    /// </summary>
    private static bool TryFindFile(string path, out string foundPath)
    {
        if (System.IO.File.Exists(path))
        {
            foundPath = path;
            return true;
        }
        var fullPath = System.IO.Path.GetFullPath(path);
        if (System.IO.File.Exists(fullPath))
        {
            foundPath = fullPath;
            return true;
        }
        foundPath = path;
        return false;
    }

    /// <summary>
    /// Loads and processes a single module file, registering its functions and variables.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _httpModuleCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Authorises a module or schema location hint for import under <paramref name="policy"/>,
    /// resolving it against the static base URI. Null (with an XQST0059 error) when refused.
    /// </summary>
    private Uri? AuthorizeImport(Security.ResourcePolicy policy, string hint, List<AnalysisError> errors, XQueryExpression import)
    {
        var baseUri = _context.BaseUri != null && Uri.TryCreate(_context.BaseUri, UriKind.Absolute, out var b) ? b : null;
        try
        {
            return policy.Authorize(hint, Security.ResourceAccessKind.ImportStylesheet, baseUri);
        }
        catch (Security.ResourceAccessDeniedException e)
        {
            errors.Add(new AnalysisError(XQueryErrorCodes.XQST0059, e.Message, import.Location));
            return null;
        }
    }

    /// <summary>
    /// Fetches an HTTP(S) XQuery module to a temp file so the existing file-based loader
    /// can consume it. Caches per-URI to avoid re-fetching when the same module is
    /// referenced multiple times in one compilation. Returns null on failure (and adds
    /// an XQST0059 error so the caller surfaces a useful diagnostic).
    /// </summary>
    /// <remarks>
    /// The process-wide cache serves only compilations with no resource policy: a cached copy
    /// says nothing about whether THIS policy allows the location (or allowed every redirect
    /// that produced it), so a policy-governed compilation always fetches, re-authorising each
    /// redirect, and never publishes to the cache.
    /// </remarks>
    private static string? DownloadHttpModuleToTempFile(
        Uri uri, List<AnalysisError> errors, ModuleImportExpression modImport, Security.ResourcePolicy? policy = null)
    {
        var key = uri.AbsoluteUri;
        if (policy is null && _httpModuleCache.TryGetValue(key, out var cachedPath) && System.IO.File.Exists(cachedPath))
            return cachedPath;

        try
        {
            Func<Uri, bool>? checkRedirect = policy is null
                ? null
                : target => policy.IsAllowed(target, Security.ResourceAccessKind.ImportStylesheet);
            var content = HttpDocumentClient.GetStringAsync(uri, checkRedirect).GetAwaiter().GetResult();
            var tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"phoenixmldb-xqm-{System.Guid.NewGuid():N}.xqm");
            System.IO.File.WriteAllText(tempPath, content);
            if (policy is null)
                _httpModuleCache[key] = tempPath;
            return tempPath;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException
            or TaskCanceledException or System.IO.IOException or Security.ResourceAccessDeniedException)
        {
            errors.Add(new AnalysisError(XQueryErrorCodes.XQST0059,
                $"Failed to fetch module from '{uri}': {ex.Message}", modImport.Location));
            return null;
        }
    }

    /// <summary>True if <paramref name="uri"/> has a '%' not followed by two hexadecimal digits.</summary>
    private static bool HasMalformedPercentEscape(string uri)
    {
        for (var i = 0; i < uri.Length; i++)
        {
            if (uri[i] != '%')
                continue;
            if (i + 2 >= uri.Length || !Uri.IsHexDigit(uri[i + 1]) || !Uri.IsHexDigit(uri[i + 2]))
                return true;
            i += 2;
        }
        return false;
    }

    /// <summary>The "XQST0113" of a parse message that begins "XQST0113: ...", or null.</summary>
    private static string? LeadingErrorCode(string message) =>
        System.Text.RegularExpressions.Regex.Match(message, "^([A-Z]{4}[0-9]{4}):") is { Success: true } m
            ? m.Groups[1].Value : null;

    /// <summary>
    /// How deep a chain of modules importing modules may go. Each level is a nested call here,
    /// so an unbounded chain is an unbounded stack: about 3,700 levels overflowed it, and a
    /// stack overflow cannot be caught — it ends the host process. A query author who can name
    /// a module location could therefore take the host down with a long enough chain of small
    /// files. No real module graph is anywhere near this deep.
    /// </summary>
    internal const int MaxModuleImportDepth = 64;

    private int _moduleImportDepth;

    // suppliedSource: the module's text when the host supplied it. modulePath is then the URI the
    // module is known by, and no file is read.
    private bool TryLoadModuleFile(string modulePath, ModuleImportExpression modImport, List<AnalysisError> errors,
        string? suppliedSource = null)
    {
        if (_moduleImportDepth >= MaxModuleImportDepth)
        {
            errors.Add(new AnalysisError(XQueryErrorCodes.XQST0059,
                $"Module '{modImport.NamespaceUri}' is imported through a chain of more than {MaxModuleImportDepth} " +
                "modules; the import graph is too deep to load.", modImport.Location));
            return false;
        }
        _moduleImportDepth++;
        try
        {
            return TryLoadModuleFileCore(modulePath, modImport, errors, suppliedSource);
        }
        finally
        {
            _moduleImportDepth--;
        }
    }

    private bool TryLoadModuleFileCore(string modulePath, ModuleImportExpression modImport, List<AnalysisError> errors,
        string? suppliedSource)
    {
        // What relative imports inside the module resolve against: the file's own location, or
        // for host-supplied text the URI the host said it is known by.
        var moduleLocation = suppliedSource != null ? modulePath : _moduleLocations.GetValueOrDefault(modulePath);
        var moduleBaseUri = moduleLocation ?? new Uri(System.IO.Path.GetFullPath(modulePath)).AbsoluteUri;
        // The module's static base URI (XQuery 3.1 §2.1.1): where it is. Under a resource
        // policy only a location the import named counts; the path of the file read can tell a
        // query where the host keeps its files.
        var moduleStaticBase = moduleLocation ?? (_context.ResourcePolicy is null ? moduleBaseUri : null);
        try
        {
            var moduleSource = suppliedSource ?? System.IO.File.ReadAllText(modulePath);
            var parser = new Parser.XQueryParserFacade();
            var moduleAst = parser.Parse(moduleSource);

            if (moduleAst is not ModuleExpression moduleExpr)
                return false;

            // XQST0088: Module namespace URI must not be empty
            // Normalize the target namespace URI (collapse whitespace per XML namespace spec)
            var normalizedTargetNs = NormalizeNamespaceUri(moduleExpr.TargetNamespace ?? "");
            if (string.IsNullOrEmpty(normalizedTargetNs))
            {
                errors.Add(new AnalysisError(XQueryErrorCodes.XQST0088,
                    "Module namespace URI must not be empty", modImport.Location));
                return false;
            }

            // XQST0046: the module namespace must be a valid URI. A malformed percent-escape such as "%gg" was accepted
            // (xquery#63, QT3 XQST0046_02).
            if (HasMalformedPercentEscape(normalizedTargetNs))
            {
                errors.Add(new AnalysisError(XQueryErrorCodes.XQST0046,
                    $"Module namespace URI '{normalizedTargetNs}' is not a valid URI (malformed percent-escape)", modImport.Location));
                return false;
            }

            // The module at this location must be a module FOR the imported namespace (XQuery 3.1
            // §4.12). A file declaring another namespace was loaded regardless and its
            // declarations filed under the imported one: its public functions were then
            // undefined, and its private ones "not accessible", in a query that never named
            // them. Such a file is simply not the module asked for; with no other location
            // that is XQST0059 (QT3 modules-bad-ns).
            if (!string.Equals(normalizedTargetNs, NormalizeNamespaceUri(modImport.NamespaceUri), StringComparison.Ordinal))
                return false;

            // Snapshot ALL namespace bindings so we can restore them after processing
            // the imported module. Namespace declarations in the imported module must NOT
            // leak to the importing module (XQuery 3.1 §4.12).
            var savedNamespaces = _context.Namespaces.SnapshotPrefixes();

            // First pass: register namespace declarations, checking for duplicates
            var seenDefaultElement = false;
            var seenDefaultFunction = false;
            var seenPrefixes = new HashSet<string>();
            foreach (var decl in moduleExpr.Declarations)
            {
                if (decl is NamespaceDeclarationExpression nsDecl)
                {
                    // XQST0066: duplicate default element/function namespace
                    if (nsDecl.Prefix == "##default-element")
                    {
                        if (seenDefaultElement)
                        {
                            errors.Add(new AnalysisError(XQueryErrorCodes.XQST0066,
                                "Duplicate default element namespace declaration", nsDecl.Location));
                            continue;
                        }
                        seenDefaultElement = true;
                    }
                    else if (nsDecl.Prefix == "##default-function")
                    {
                        if (seenDefaultFunction)
                        {
                            errors.Add(new AnalysisError(XQueryErrorCodes.XQST0066,
                                "Duplicate default function namespace declaration", nsDecl.Location));
                            continue;
                        }
                        seenDefaultFunction = true;
                    }
                    else if (!nsDecl.Prefix.StartsWith('#'))
                    {
                        // XQST0033: duplicate namespace prefix
                        if (!seenPrefixes.Add(nsDecl.Prefix))
                        {
                            errors.Add(new AnalysisError(XQueryErrorCodes.XQST0033,
                                $"Duplicate namespace declaration for prefix '{nsDecl.Prefix}'", nsDecl.Location));
                            continue;
                        }
                    }
                    // Normalize the namespace URI for consistent resolution
                    var normalizedNsDeclUri = NormalizeNamespaceUri(nsDecl.Uri);
                    _context.Namespaces.RegisterNamespace(nsDecl.Prefix, normalizedNsDeclUri);
                }
            }

            // XQST0048: All variables and functions declared in a library module must be
            // in the module's target namespace. Check after namespace declarations are registered.
            foreach (var decl in moduleExpr.Declarations)
            {
                if (decl is VariableDeclarationExpression vd && vd.Name.Prefix != null)
                {
                    var varNsUri = _context.Namespaces.ResolvePrefix(vd.Name.Prefix);
                    if (varNsUri != null && varNsUri != normalizedTargetNs)
                    {
                        errors.Add(new AnalysisError(XQueryErrorCodes.XQST0048,
                            $"Variable ${vd.Name} is in namespace '{varNsUri}' but module target namespace is '{normalizedTargetNs}'",
                            modImport.Location));
                        _context.Namespaces.RestorePrefixes(savedNamespaces);
                        return false;
                    }
                }
                // Functions too, as the comment above always said: bar:foo() declared in module namespace foo was accepted
                // (xquery#63, QT3 XQST0048).
                else if (decl is FunctionDeclarationExpression fd && fd.Name.Prefix != null)
                {
                    var fnNsUri = _context.Namespaces.ResolvePrefix(fd.Name.Prefix);
                    if (fnNsUri != null && fnNsUri != normalizedTargetNs)
                    {
                        errors.Add(new AnalysisError(XQueryErrorCodes.XQST0048,
                            $"Function {fd.Name}#{fd.Parameters.Count} is in namespace '{fnNsUri}' but module target namespace is '{normalizedTargetNs}'",
                            modImport.Location));
                        _context.Namespaces.RestorePrefixes(savedNamespaces);
                        return false;
                    }
                }
            }

            // Second pass: register functions, variables, and resolve nested module imports
            foreach (var decl in moduleExpr.Declarations)
            {
                switch (decl)
                {
                    case FunctionDeclarationExpression funcDecl:
                        var resolvedName = ResolveQName(funcDecl.Name);
                        if (!resolvedName.Equals(funcDecl.Name) || resolvedName.Prefix != funcDecl.Name.Prefix)
                            funcDecl.Name = resolvedName;
                        // XQST0034: check for duplicate function across merged modules
                        // (same namespace, multiple files defining the same function name+arity)
                        var existingFn = _context.Functions.Resolve(funcDecl.Name, funcDecl.Parameters.Count);
                        if (existingFn is DeclaredFunctionPlaceholder existingPlaceholder
                            && existingPlaceholder.IsFromImportedModule)
                        {
                            errors.Add(new AnalysisError(XQueryErrorCodes.XQST0034,
                                $"Duplicate function declaration: {funcDecl.Name}#{funcDecl.Parameters.Count} " +
                                $"is defined in multiple modules for the same namespace",
                                modImport.Location));
                            _context.Namespaces.RestorePrefixes(savedNamespaces);
                            return false;
                        }
                        // Register all functions (including private ones) — the FunctionResolver
                        // will check IsModulePrivate to block external access, but private
                        // functions must be accessible within the module's own function bodies.
                        if (funcDecl.IsExternal)
                        {
                            if (!TryBindExternalFunction(funcDecl, errors))
                            {
                                _context.Namespaces.RestorePrefixes(savedNamespaces);
                                return false;
                            }
                            break;
                        }
                        _context.Functions.Register(new DeclaredFunctionPlaceholder(funcDecl, isFromImportedModule: true));
                        break;

                    case VariableDeclarationExpression varDecl:
                        var resolvedVarName = ResolveQName(varDecl.Name);
                        if (resolvedVarName != varDecl.Name)
                            varDecl.Name = resolvedVarName;
                        // XQST0049: check for duplicate variable across merged modules
                        var varKey = _context.MakeVariableKey(varDecl.Name);
                        if (_context.GlobalVariables.ContainsKey(varKey))
                        {
                            errors.Add(new AnalysisError(XQueryErrorCodes.XQST0049,
                                $"Duplicate variable declaration: ${varDecl.Name} " +
                                $"is defined in multiple modules for the same namespace",
                                modImport.Location));
                            _context.Namespaces.RestorePrefixes(savedNamespaces);
                            return false;
                        }
                        _context.RegisterGlobalVariable(varDecl.Name, varDecl.TypeDeclaration, varDecl.IsPrivate);
                        break;

                    case SchemaImportExpression moduleSchemaImport:
                        // Relative hints are relative to this module, as for a nested module import.
                        var savedSchemaBaseUri = _context.BaseUri;
                        try
                        {
                            _context.BaseUri = moduleBaseUri;
                            LoadSchemaImport(moduleSchemaImport, errors);
                        }
                        finally
                        {
                            _context.BaseUri = savedSchemaBaseUri;
                        }
                        break;

                    case ModuleImportExpression nestedModImport:
                        // Register the namespace prefix from nested import
                        if (nestedModImport.Prefix != null)
                            _context.Namespaces.RegisterNamespace(nestedModImport.Prefix, nestedModImport.NamespaceUri);
                        // Recursively resolve nested module imports. The nested import's
                        // relative `at` URI must resolve against THIS module's location,
                        // not the importing analyzer's base URI (Martin Honnen 2026-05-20,
                        // load-xquery-module of module2 which itself imports module1 by
                        // relative path — without swapping base URI the placeholder for
                        // f1:foo never got replaced and runtime invocation crashed).
                        var savedBaseUri = _context.BaseUri;
                        var (savedLocation, savedInLibrary) = (_libraryModuleLocation, _inLibraryModule);
                        try
                        {
                            _context.BaseUri = moduleBaseUri;
                            (_libraryModuleLocation, _inLibraryModule) = (moduleStaticBase, true);
                            TryResolveModule(nestedModImport, errors);
                        }
                        finally
                        {
                            _context.BaseUri = savedBaseUri;
                            (_libraryModuleLocation, _inLibraryModule) = (savedLocation, savedInLibrary);
                        }
                        break;
                }
            }

            // Pre-bind ALL names (function calls, function refs, variable references)
            // in the imported module's AST using the module's namespace context.
            // This resolves prefixed names like $mod:var and mod:func() before the
            // module's namespace bindings are restored away.
            var importedDefaultFnNs = _context.Namespaces.ResolvePrefix("##default-function");
            var defaultFnNsId = !string.IsNullOrEmpty(importedDefaultFnNs)
                ? _context.Namespaces.GetOrCreateId(importedDefaultFnNs)
                : Core.NamespaceId.None;
            PrebindModuleNames(moduleExpr, defaultFnNsId, importedDefaultFnNs);

            // Element names (in constructors and in path name tests) resolve in the MODULE's
            // static context too: its default element namespace, declared with
            // `declare default element namespace` or `import schema default element namespace`,
            // applies to <abf/> in its function bodies. They were left for the importing query's
            // pass, which no longer sees the module's declarations, so every unprefixed element
            // a module built or selected was in no namespace (fn-load-xquery-module-051..057).
            if (new NamespaceResolver(_context.Namespaces).Resolve(moduleExpr, errors) is ModuleExpression resolvedModule)
                moduleExpr = resolvedModule;

            // Each declaration runs with the base URI of the file it is in: `declare base-uri`
            // when the module has one (a relative one is relative to where the module is),
            // and otherwise where the module is. It was the importing query's base URI, so a
            // relative URI in a library module resolved against the main module.
            var declaredBase = moduleExpr.BaseUri;
            var effectiveBase = string.IsNullOrEmpty(declaredBase) ? moduleStaticBase
                : moduleStaticBase != null && Uri.TryCreate(moduleStaticBase, UriKind.Absolute, out var locationUri)
                    && Uri.TryCreate(locationUri, declaredBase, out var resolvedBase) ? resolvedBase.AbsoluteUri
                : declaredBase;
            foreach (var declaration in moduleExpr.Declarations)
            {
                if (declaration is FunctionDeclarationExpression { InLibraryModule: false } function)
                {
                    function.ModuleBaseUri ??= effectiveBase;
                    // Where the module is, whatever base URI it declares.
                    function.ModuleLocation = moduleStaticBase;
                    function.InLibraryModule = true;
                }
                else if (declaration is VariableDeclarationExpression { InLibraryModule: false } variable)
                {
                    variable.ModuleBaseUri ??= effectiveBase;
                    variable.ModuleLocation = moduleStaticBase;
                    variable.InLibraryModule = true;
                }
            }

            // If another module file for the same namespace was already loaded,
            // merge declarations rather than overwriting.
            if (_context.ImportedModules.TryGetValue(modImport.NamespaceUri, out var existingModule))
            {
                var merged = new List<XQueryExpression>(existingModule.Declarations);
                merged.AddRange(moduleExpr.Declarations);
                _context.ImportedModules[modImport.NamespaceUri] = new ModuleExpression
                {
                    Declarations = merged,
                    Body = existingModule.Body,
                    Location = existingModule.Location,
                    TargetNamespace = existingModule.TargetNamespace,
                    BaseUri = existingModule.BaseUri ?? moduleExpr.BaseUri
                };
            }
            else
            {
                _context.ImportedModules[modImport.NamespaceUri] = moduleExpr;
            }

            // Restore ALL namespace bindings to prevent the imported module's
            // namespace declarations from leaking into the importing module.
            _context.Namespaces.RestorePrefixes(savedNamespaces);

            return true;
        }
        catch (Parser.XQueryParseException ex) when (LeadingErrorCode(ex.Message) is { } code)
        {
            // The module was found but is statically invalid: that is the module's own error
            // (e.g. XQST0113 for a context item value in a library module, QT3
            // contextDecl-048/052), not XQST0059 "module not found".
            errors.Add(new AnalysisError(code, $"In module '{modulePath}': {ex.Message}", modImport.Location));
        }
        catch (Exception ex)
        {
            errors.Add(new AnalysisError(
                XQueryErrorCodes.XQST0059,
                $"Error loading module from '{modulePath}': {ex.Message}",
                modImport.Location));
        }

        return false;
    }

    /// <summary>
    /// Walks an imported library module's AST and pre-resolves all prefixed and unprefixed names
    /// (function calls, function refs, variable references) using the module's namespace context.
    /// This prevents namespace leaking — the module's prefixes are only used during this pass,
    /// then restored away so they don't affect the importing module.
    /// </summary>
    private void PrebindModuleNames(XQueryExpression root, Core.NamespaceId defaultFnNsId, string? defaultFnNsUri)
    {
        var walker = new ModuleNameRebinder(_context.Namespaces, defaultFnNsId, defaultFnNsUri);
        walker.Walk(root);
    }

    private sealed class ModuleNameRebinder : XQueryExpressionWalker
    {
        private readonly NamespaceContext _ns;
        private readonly Core.NamespaceId _defaultFnNsId;
        private readonly string? _defaultFnNsUri;

        public ModuleNameRebinder(NamespaceContext ns, Core.NamespaceId defaultFnNsId, string? defaultFnNsUri)
        {
            _ns = ns;
            _defaultFnNsId = defaultFnNsId;
            _defaultFnNsUri = defaultFnNsUri;
        }

        public override object? VisitFunctionCallExpression(FunctionCallExpression expr)
        {
            if (expr.Name.Namespace == Core.NamespaceId.None)
            {
                if (expr.Name.Prefix == null)
                {
                    // Unprefixed → default function namespace
                    if (_defaultFnNsId != Core.NamespaceId.None)
                        expr.Name = new Core.QName(_defaultFnNsId, expr.Name.LocalName) { RuntimeNamespace = _defaultFnNsUri };
                }
                else if (expr.Name.Prefix.Length > 0)
                {
                    // Prefixed → resolve prefix
                    var uri = _ns.ResolvePrefix(expr.Name.Prefix);
                    if (uri != null)
                    {
                        var nsId = _ns.GetOrCreateId(uri);
                        expr.Name = new Core.QName(nsId, expr.Name.LocalName, expr.Name.Prefix) { ExpandedNamespace = uri };
                    }
                }
            }
            return base.VisitFunctionCallExpression(expr);
        }

        public override object? VisitNamedFunctionRef(NamedFunctionRef expr)
        {
            if (expr.Name.Namespace == Core.NamespaceId.None)
            {
                if (expr.Name.Prefix == null)
                {
                    if (_defaultFnNsId != Core.NamespaceId.None)
                        expr.Name = new Core.QName(_defaultFnNsId, expr.Name.LocalName) { RuntimeNamespace = _defaultFnNsUri };
                }
                else if (expr.Name.Prefix.Length > 0)
                {
                    var uri = _ns.ResolvePrefix(expr.Name.Prefix);
                    if (uri != null)
                    {
                        var nsId = _ns.GetOrCreateId(uri);
                        expr.Name = new Core.QName(nsId, expr.Name.LocalName, expr.Name.Prefix) { ExpandedNamespace = uri };
                    }
                }
            }
            return base.VisitNamedFunctionRef(expr);
        }

        public override object? VisitVariableReference(VariableReference expr)
        {
            if (expr.Name.Prefix != null && expr.Name.Prefix.Length > 0
                && expr.Name.Namespace == Core.NamespaceId.None
                && string.IsNullOrEmpty(expr.Name.ExpandedNamespace))
            {
                var uri = _ns.ResolvePrefix(expr.Name.Prefix);
                if (uri != null)
                {
                    var nsId = _ns.GetOrCreateId(uri);
                    expr.Name = new Core.QName(nsId, expr.Name.LocalName, expr.Name.Prefix) { ExpandedNamespace = uri };
                }
            }
            return base.VisitVariableReference(expr);
        }
    }

    private static bool IsReservedFunctionNamespace(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return false;
        return uri == "http://www.w3.org/XML/1998/namespace"
            || uri == "http://www.w3.org/2001/XMLSchema"
            || uri == "http://www.w3.org/2001/XMLSchema-instance"
            || uri == "http://www.w3.org/2005/xpath-functions"
            || uri == "http://www.w3.org/2005/xpath-functions/math"
            || uri == "http://www.w3.org/2005/xpath-functions/map"
            || uri == "http://www.w3.org/2005/xpath-functions/array"
            // PhoeniXML extension functions: no query or library module may declare its own phx: functions.
            || uri == WellKnownNamespaces.PhxUri;
    }

    /// <summary>
    /// Resolves a QName's prefix to a namespace ID using the static context.
    /// </summary>
    /// <summary>
    /// Resolves a variable QName: prefix → nsId and EQName Q{uri} → nsId.
    /// Does NOT apply the default function namespace (variables have no default namespace).
    /// </summary>
    private QName ResolveVariableQName(QName name)
    {
        if (!string.IsNullOrEmpty(name.ExpandedNamespace) && name.Namespace == Core.NamespaceId.None)
        {
            var eqNsId = _context.Namespaces.GetOrCreateId(name.ExpandedNamespace);
            return new QName(eqNsId, name.LocalName, name.Prefix) { ExpandedNamespace = name.ExpandedNamespace };
        }
        if (string.IsNullOrEmpty(name.Prefix)) return name;
        var uri = _context.Namespaces.ResolvePrefix(name.Prefix!);
        if (uri == null) return name;
        var nsId = _context.Namespaces.GetOrCreateId(uri);
        return new QName(nsId, name.LocalName, name.Prefix) { ExpandedNamespace = uri };
    }

    private QName ResolveQName(QName name)
    {
        // EQName form Q{uri}local: Prefix is "" (empty, not null), ExpandedNamespace is set.
        // Populate Namespace ID so runtime QName equality (by nsId + local) matches prefixed
        // references that resolve to the same URI.
        if (!string.IsNullOrEmpty(name.ExpandedNamespace) && name.Namespace == Core.NamespaceId.None)
        {
            var eqNsId = _context.Namespaces.GetOrCreateId(name.ExpandedNamespace);
            return new QName(eqNsId, name.LocalName, name.Prefix) { ExpandedNamespace = name.ExpandedNamespace };
        }
        if (name.Prefix == null)
        {
            // Unprefixed user function declaration: apply default function namespace if set.
            if (name.Namespace != Core.NamespaceId.None) return name;
            var defaultUri = _context.Namespaces.ResolvePrefix("##default-function");
            if (string.IsNullOrEmpty(defaultUri)) return name;
            var defaultNsId = _context.Namespaces.GetOrCreateId(defaultUri);
            return new QName(defaultNsId, name.LocalName) { RuntimeNamespace = defaultUri };
        }
        if (name.Prefix.Length == 0) return name;
        var uri = _context.Namespaces.ResolvePrefix(name.Prefix);
        if (uri == null) return name;
        var nsId = _context.Namespaces.GetOrCreateId(uri);
        return new QName(nsId, name.LocalName, name.Prefix) { ExpandedNamespace = uri };
    }

    /// <summary>
    /// Injects function and variable declarations from imported modules into the main module's
    /// declarations list so the optimizer creates physical operators for them.
    /// </summary>
    /// <summary>
    /// Rewrites a decimal-format name to its EQName Q{uri}local form using the
    /// imported module's prolog namespace bindings, so the format key matches what
    /// prefix resolution inside the imported function's body will produce at lookup
    /// time. A null/empty name (the default decimal-format) is left null — default
    /// decimal-formats from imported modules are intentionally not propagated
    /// (the importing module's default wins).
    /// </summary>
    private static string? ReKeyDecimalFormatNameToEQName(string? formatName, ModuleExpression importedModule)
    {
        if (string.IsNullOrEmpty(formatName)) return null;
        // EQName forms are already canonical.
        if (formatName.StartsWith("Q{", StringComparison.Ordinal)) return formatName;
        var colonIdx = formatName.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx <= 0)
        {
            // Plain NCName (no prefix). Per XQuery 4.0 §4.18, decimal-format declarations
            // are module-local. Re-key using the module's target namespace so it doesn't
            // collide with a same-named format in the importing module (decimal-format-21).
            if (!string.IsNullOrEmpty(importedModule.TargetNamespace))
                return $"Q{{{importedModule.TargetNamespace}}}{formatName}";
            return formatName;
        }
        var prefix = formatName[..colonIdx];
        var local = formatName[(colonIdx + 1)..];
        foreach (var d in importedModule.Declarations)
        {
            if (d is NamespaceDeclarationExpression nsDecl
                && string.Equals(nsDecl.Prefix, prefix, StringComparison.Ordinal))
            {
                return $"Q{{{nsDecl.Uri}}}{local}";
            }
        }
        // Last resort: if the format prefix is the module's own target namespace
        // (most common: declare decimal-format mod:foo where mod is the module's
        // declared prefix), use TargetNamespace directly.
        if (!string.IsNullOrEmpty(importedModule.TargetNamespace))
        {
            return $"Q{{{importedModule.TargetNamespace}}}{local}";
        }
        return formatName;
    }

    /// <summary>A relative location hint resolved against the static base URI, or null.</summary>
    private string? ResolveAgainstBase(string hint)
        => !Uri.TryCreate(hint, UriKind.Absolute, out _)
           && _context.BaseUri != null
           && Uri.TryCreate(_context.BaseUri, UriKind.Absolute, out var baseUri)
           && Uri.TryCreate(baseUri, hint, out var resolved)
            ? resolved.AbsoluteUri
            : null;

    private XQueryExpression InjectImportedDeclarations(XQueryExpression expression)
    {
        if (expression is not ModuleExpression module || _context.ImportedModules.Count == 0)
            return expression;

        // Imported module declarations must come BEFORE the main module's declarations
        // so that imported variables are bound before main module variables that may
        // reference imported functions (which in turn reference imported variables).
        var importedDecls = new List<XQueryExpression>();
        // Imported context-item declarations are appended AFTER main's so the main
        // module's :=value (or env-supplied external) is already bound when each
        // imported module type-checks it (contextDecl-050/051/054 — XPTY0004
        // raised when any imported module's declared type disagrees).
        var importedContextItemDecls = new List<XQueryExpression>();
        foreach (var (_, importedModule) in _context.ImportedModules)
        {
            var moduleBaseUri = importedModule.BaseUri;

            // Temporarily register the module's internal namespace declarations so that
            // QName resolution of $mod:var and mod:func works correctly. These prefixes
            // were restored away after module loading to prevent namespace leaking.
            var savedNs = _context.Namespaces.SnapshotPrefixes();
            foreach (var decl in importedModule.Declarations)
            {
                if (decl is NamespaceDeclarationExpression nsDecl && !nsDecl.Prefix.StartsWith('#'))
                    _context.Namespaces.RegisterNamespace(nsDecl.Prefix, NormalizeNamespaceUri(nsDecl.Uri));
            }

            // Pre-resolve every namespace prefix inside the imported declarations
            // while the library module's own prefix bindings are still active. The
            // aggregate NamespaceResolver pass runs later against the main module's
            // namespace context — by then `declare namespace sch = "..."` from the
            // library has been restored away and `as element(sch:schema)` in a lib
            // signature would raise XPST0081 (Martin Honnen 2026-05-17).
            // Running the resolver here bakes the prefix→NamespaceId resolution
            // onto the AST nodes so they survive RestorePrefixes.
            var nsResolver = new NamespaceResolver(_context.Namespaces);
            var nsResolveErrors = new List<AnalysisError>();

            // The module's own prefixes, for names its functions resolve at run time. Those
            // resolved against the MAIN module's bindings, where `local` (say) is the predeclared
            // local-functions namespace and not the module's (QT3 fn-load-xquery-module-040).
            var modulePrefixes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var d in importedModule.Declarations)
            {
                switch (d)
                {
                    case NamespaceDeclarationExpression n when !n.Prefix.StartsWith('#'):
                        modulePrefixes[n.Prefix] = NormalizeNamespaceUri(n.Uri);
                        break;
                    case ModuleImportExpression mi when !string.IsNullOrEmpty(mi.Prefix):
                        modulePrefixes[mi.Prefix] = mi.NamespaceUri;
                        break;
                    case SchemaImportExpression si when !string.IsNullOrEmpty(si.Prefix):
                        modulePrefixes[si.Prefix] = si.TargetNamespace;
                        break;
                }
            }

            foreach (var decl in importedModule.Declarations)
            {
                if (decl is FunctionDeclarationExpression funcDecl)
                {
                    var resolvedName = ResolveQName(funcDecl.Name);
                    var resolvedFunc = (FunctionDeclarationExpression)nsResolver.Resolve(funcDecl, nsResolveErrors);
                    // Per XQuery §4.4, element constructors in a library module use the module's
                    // copy-namespaces declaration (or the default preserve/inherit if not declared),
                    // not the importing query's. We tag the function so the runtime can restore
                    // the correct mode around the function body.
                    var moduleCopyNsMode = importedModule.CopyNamespacesMode
                        ?? CopyNamespacesMode.PreserveInherit;
                    if (resolvedName != resolvedFunc.Name)
                    {
                        // Build renamed declaration with module metadata including copy-ns mode.
                        var renamedDecl = new FunctionDeclarationExpression
                        {
                            Name = resolvedName,
                            Parameters = resolvedFunc.Parameters,
                            ReturnType = resolvedFunc.ReturnType,
                            Body = resolvedFunc.Body,
                            IsPrivate = resolvedFunc.IsPrivate,
                            Location = resolvedFunc.Location,
                            ModuleBaseUri = funcDecl.ModuleBaseUri ?? moduleBaseUri,
                            ModuleLocation = funcDecl.ModuleLocation,
                            InLibraryModule = true,
                            ModuleTargetNamespace = importedModule.TargetNamespace,
                            ModuleCopyNamespacesMode = moduleCopyNsMode,
                            ModulePrefixBindings = modulePrefixes
                        };
                        importedDecls.Add(renamedDecl);
                    }
                    else
                    {
                        if (resolvedFunc.ModuleBaseUri == null && (funcDecl.ModuleBaseUri ?? moduleBaseUri) is { } functionBase)
                            resolvedFunc.ModuleBaseUri = functionBase;
                        resolvedFunc.ModuleLocation = funcDecl.ModuleLocation;
                        resolvedFunc.InLibraryModule = true;
                        if (!string.IsNullOrEmpty(importedModule.TargetNamespace)
                            && resolvedFunc.ModuleTargetNamespace == null)
                            resolvedFunc.ModuleTargetNamespace = importedModule.TargetNamespace;
                        resolvedFunc.ModuleCopyNamespacesMode ??= moduleCopyNsMode;
                        resolvedFunc.ModulePrefixBindings ??= modulePrefixes;
                        importedDecls.Add(resolvedFunc);
                    }
                }
                else if (decl is VariableDeclarationExpression varDecl)
                {
                    var resolvedVarName = ResolveQName(varDecl.Name);
                    if (resolvedVarName != varDecl.Name)
                        varDecl.Name = resolvedVarName;
                    var resolvedVar = (VariableDeclarationExpression)nsResolver.Resolve(varDecl, nsResolveErrors);
                    if (resolvedVar.ModuleBaseUri == null && (varDecl.ModuleBaseUri ?? moduleBaseUri) is { } variableBase)
                        resolvedVar.ModuleBaseUri = variableBase;
                    resolvedVar.ModuleLocation = varDecl.ModuleLocation;
                    resolvedVar.InLibraryModule = true;
                    importedDecls.Add(resolvedVar);
                }
                else if (decl is ContextItemDeclarationExpression ctxDecl)
                {
                    importedContextItemDecls.Add(ctxDecl);
                }
                else if (decl is DecimalFormatDeclarationExpression dfDecl)
                {
                    // Per XQuery 3.1 §4.18, decimal-format declarations are module-local
                    // — they are NOT exported to importing modules. But functions DEFINED
                    // in the imported module must still see their own module's formats
                    // when called from the importing module. We re-key the imported
                    // format under its EQName Q{uri}local so that prefix-resolution
                    // inside the imported function's body (which uses the imported
                    // module's prefix bindings → the lib URI) finds it, while the
                    // importing module's own code (which doesn't bind that prefix to
                    // the lib URI) can't reach it by name.
                    var rekeyed = new DecimalFormatDeclarationExpression
                    {
                        FormatName = ReKeyDecimalFormatNameToEQName(dfDecl.FormatName, importedModule),
                        Properties = dfDecl.Properties,
                        Location = dfDecl.Location
                    };
                    importedDecls.Add(rekeyed);
                }
            }

            // nsResolveErrors are dropped — the aggregate NamespaceResolver pass
            // will surface any real unbound-prefix issues with main-module context.

            // Restore namespace context — module internal prefixes must not leak
            _context.Namespaces.RestorePrefixes(savedNs);
        }

        // Prepend imported declarations before main module declarations.
        // Imported context-item declarations are appended at the END (after main's
        // declarations) so main's context-item binding is already in place when
        // imported modules type-check it.
        var augmented = new List<XQueryExpression>(
            importedDecls.Count + module.Declarations.Count + importedContextItemDecls.Count);
        augmented.AddRange(importedDecls);
        augmented.AddRange(module.Declarations);
        augmented.AddRange(importedContextItemDecls);

        return new ModuleExpression
        {
            Declarations = augmented,
            Body = module.Body,
            Location = module.Location,
            BaseUri = module.BaseUri,
            // Preserve main-module prolog settings when recreating the ModuleExpression.
            // These were lost in earlier versions, breaking `declare copy-namespaces ...`
            // for any query that imports a module (QT3 nscons-036/037/038).
            CopyNamespacesMode = module.CopyNamespacesMode,
            ConstructionMode = module.ConstructionMode,
            DefaultCollation = module.DefaultCollation,
            BoundarySpacePreserve = module.BoundarySpacePreserve,
            TargetNamespace = module.TargetNamespace,
            SchemaTypedSequenceTypes = module.SchemaTypedSequenceTypes,
            SchemaKindTestTypes = module.SchemaKindTestTypes,
        };
    }

    /// <summary>
    /// Extracts user-defined function and variable declarations from the prolog
    /// and registers them in the static context so they're available during analysis.
    /// </summary>
    private void PreRegisterDeclarations(XQueryExpression expression, List<AnalysisError> errors)
    {
        if (expression is not ModuleExpression module) return;

        var seenDefaultElement = false;
        var seenDefaultFunction = false;
        var seenPrefixes = new HashSet<string>();
        var importedModuleNamespaces = new HashSet<string>();

        foreach (var decl in module.Declarations)
        {
            switch (decl)
            {
                case NamespaceDeclarationExpression nsDecl:
                    // Check for duplicate default/prefix declarations
                    if (nsDecl.Prefix == "##default-element")
                    {
                        if (seenDefaultElement)
                        { errors.Add(new AnalysisError(XQueryErrorCodes.XQST0066, "Duplicate default element namespace declaration", nsDecl.Location)); continue; }
                        seenDefaultElement = true;
                    }
                    else if (nsDecl.Prefix == "##default-function")
                    {
                        if (seenDefaultFunction)
                        { errors.Add(new AnalysisError(XQueryErrorCodes.XQST0066, "Duplicate default function namespace declaration", nsDecl.Location)); continue; }
                        seenDefaultFunction = true;
                    }
                    else if (!nsDecl.Prefix.StartsWith('#'))
                    {
                        if (!seenPrefixes.Add(nsDecl.Prefix))
                        { errors.Add(new AnalysisError(XQueryErrorCodes.XQST0033, $"Duplicate namespace declaration for prefix '{nsDecl.Prefix}'", nsDecl.Location)); continue; }
                        // XQST0070: cannot rebind 'xml' prefix to anything other than the XML namespace
                        if (nsDecl.Prefix == "xml" && nsDecl.Uri != "http://www.w3.org/XML/1998/namespace")
                        { errors.Add(new AnalysisError(XQueryErrorCodes.XQST0070, "Cannot rebind the 'xml' prefix", nsDecl.Location)); continue; }
                        if (nsDecl.Prefix == "xml" && string.IsNullOrEmpty(nsDecl.Uri))
                        { errors.Add(new AnalysisError(XQueryErrorCodes.XQST0070, "Cannot undeclare the 'xml' prefix", nsDecl.Location)); continue; }
                        // XQST0070: cannot bind 'xmlns' prefix
                        if (nsDecl.Prefix == "xmlns")
                        { errors.Add(new AnalysisError(XQueryErrorCodes.XQST0070, "Cannot declare the 'xmlns' prefix", nsDecl.Location)); continue; }
                    }
                    _context.Namespaces.RegisterNamespace(nsDecl.Prefix, nsDecl.Uri);
                    break;

                case ModuleImportExpression modImport:
                    // XQST0070: Cannot use 'xml' or 'xmlns' as the module import prefix
                    if (modImport.Prefix == "xml")
                    {
                        errors.Add(new AnalysisError(XQueryErrorCodes.XQST0070,
                            "Cannot use 'xml' as a module import prefix",
                            modImport.Location));
                        break;
                    }
                    if (modImport.Prefix == "xmlns")
                    {
                        errors.Add(new AnalysisError(XQueryErrorCodes.XQST0070,
                            "Cannot use 'xmlns' as a module import prefix",
                            modImport.Location));
                        break;
                    }

                    // Normalize namespace URI: strip leading/trailing whitespace,
                    // collapse internal whitespace sequences to single spaces (XQuery 3.1 §4.12)
                    var normalizedUri = NormalizeNamespaceUri(modImport.NamespaceUri);

                    // XQST0047: duplicate module target namespace — same namespace imported twice
                    if (!importedModuleNamespaces.Add(normalizedUri))
                    {
                        errors.Add(new AnalysisError(XQueryErrorCodes.XQST0047,
                            $"Module namespace '{normalizedUri}' is imported more than once",
                            modImport.Location));
                        break;
                    }

                    // Register the namespace prefix binding so downstream analysis can resolve it
                    if (modImport.Prefix != null)
                        _context.Namespaces.RegisterNamespace(modImport.Prefix, normalizedUri);

                    // Create a normalized copy if the URI was changed by normalization
                    var effectiveImport = normalizedUri != modImport.NamespaceUri
                        ? new ModuleImportExpression
                        {
                            Prefix = modImport.Prefix,
                            NamespaceUri = normalizedUri,
                            LocationHints = modImport.LocationHints,
                            Location = modImport.Location
                        }
                        : modImport;

                    // XQST0088: the target namespace of an import must not be empty. It was only
                    // checked on a module once one had been found, so `import module ""` reported
                    // that no module could be resolved instead (QT3 K-ModuleImport-1/2, XQST0088_1).
                    if (string.IsNullOrEmpty(normalizedUri))
                    {
                        errors.Add(new AnalysisError(XQueryErrorCodes.XQST0088,
                            "The target namespace of a module import must not be the empty string", modImport.Location));
                        continue;
                    }

                    // Resolve and load the library module from location hints
                    if (!TryResolveModule(effectiveImport, errors))
                    {
                        errors.Add(new AnalysisError(
                            XQueryErrorCodes.XQST0059,
                            $"Module '{normalizedUri}' could not be resolved from location hints: [{string.Join(", ", modImport.LocationHints)}]",
                            modImport.Location));
                    }
                    break;

                case SchemaImportExpression schemaImport:
                    LoadSchemaImport(schemaImport, errors);
                    break;
            }
        }

        // Second pass: register functions and variables (after namespaces are set up)
        // Track what functions/variables were imported to detect XQST0034/XQST0049 collisions
        var importedFunctions = new HashSet<string>(); // "ns:local#arity"
        var importedVariables = new HashSet<string>(); // "ns:local"
        var declaredVariables = new HashSet<string>(); // track main-module vars for XQST0049 duplicate detection

        // Collect imported function/variable names from all imported modules
        foreach (var kv in _context.ImportedModules)
        {
            foreach (var decl in kv.Value.Declarations)
            {
                if (decl is FunctionDeclarationExpression fd && !fd.IsPrivate)
                {
                    var fnNs = fd.Name.Prefix != null
                        ? (_context.Namespaces.ResolvePrefix(fd.Name.Prefix) ?? "")
                        : "";
                    importedFunctions.Add($"{fnNs}:{fd.Name.LocalName}#{fd.Parameters.Count}");
                }
                else if (decl is VariableDeclarationExpression vd && !vd.IsPrivate)
                {
                    var varNs = vd.Name.Prefix != null
                        ? (_context.Namespaces.ResolvePrefix(vd.Name.Prefix) ?? "")
                        : "";
                    importedVariables.Add($"{varNs}:{vd.Name.LocalName}");
                }
            }
        }

        foreach (var decl in module.Declarations)
        {
            switch (decl)
            {
                case FunctionDeclarationExpression funcDecl:
                    // Resolve namespace prefix on function name
                    var resolvedName = ResolveQName(funcDecl.Name);
                    // XQST0045: user functions cannot be declared in reserved namespaces
                    // Use EQName's expanded namespace if available, then prefix resolution, then default
                    var fnUri = funcDecl.Name.ExpandedNamespace
                        ?? (funcDecl.Name.Prefix != null
                            ? _context.Namespaces.ResolvePrefix(funcDecl.Name.Prefix)
                            : (_context.Namespaces.ResolvePrefix("##default-function") ?? _context.DefaultFunctionNamespace));
                    if (IsReservedFunctionNamespace(fnUri))
                    {
                        errors.Add(new AnalysisError(
                            XQueryErrorCodes.XQST0045,
                            $"Function {funcDecl.Name.LocalName} cannot be declared in reserved namespace '{fnUri}'",
                            funcDecl.Location));
                        break;
                    }
                    // Mutate the declaration's name in-place so the optimizer and runtime
                    // see the resolved name consistently with resolved call sites.
                    if (!resolvedName.Equals(funcDecl.Name) || resolvedName.Prefix != funcDecl.Name.Prefix)
                        funcDecl.Name = resolvedName;

                    // XQST0034: function name collides with imported function of same name/arity
                    var fnKey = $"{fnUri ?? ""}:{funcDecl.Name.LocalName}#{funcDecl.Parameters.Count}";
                    if (importedFunctions.Contains(fnKey))
                    {
                        errors.Add(new AnalysisError(XQueryErrorCodes.XQST0034,
                            $"Function {funcDecl.Name.LocalName}#{funcDecl.Parameters.Count} collides with an imported function",
                            funcDecl.Location));
                        break;
                    }

                    if (funcDecl.IsExternal)
                    {
                        TryBindExternalFunction(funcDecl, errors);
                        break;
                    }

                    var placeholder = new DeclaredFunctionPlaceholder(funcDecl);
                    _context.Functions.Register(placeholder);
                    break;

                case VariableDeclarationExpression varDecl:
                {
                    // Resolve variable-name prefix (but NOT default-function namespace — variables
                    // have no default namespace per XQuery 3.1 §4.14).
                    var resolvedVarName = ResolveVariableQName(varDecl.Name);
                    if (!resolvedVarName.Equals(varDecl.Name) || resolvedVarName.Prefix != varDecl.Name.Prefix)
                        varDecl.Name = resolvedVarName;

                    // XQST0049: variable name collides with imported variable or duplicate in same module
                    var varNsUri = varDecl.Name.Prefix != null
                        ? (_context.Namespaces.ResolvePrefix(varDecl.Name.Prefix) ?? "")
                        : "";
                    var varKey = $"{varNsUri}:{varDecl.Name.LocalName}";
                    if (importedVariables.Contains(varKey))
                    {
                        errors.Add(new AnalysisError(XQueryErrorCodes.XQST0049,
                            $"Variable ${varDecl.Name} collides with an imported variable",
                            varDecl.Location));
                        break;
                    }
                    if (!declaredVariables.Add(varKey))
                    {
                        errors.Add(new AnalysisError(XQueryErrorCodes.XQST0049,
                            $"Duplicate variable declaration: ${varDecl.Name}",
                            varDecl.Location));
                        break;
                    }

                    _context.RegisterGlobalVariable(varDecl.Name, varDecl.TypeDeclaration);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Normalizes a namespace URI per XML namespace specification:
    /// strips leading/trailing whitespace and collapses internal whitespace sequences
    /// to single spaces.
    /// </summary>
    private static string NormalizeNamespaceUri(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return uri;
        // Strip leading/trailing whitespace (including \t, \n, \r)
        var trimmed = uri.Trim();
        // Collapse internal whitespace sequences to single space
        if (trimmed.IndexOfAny([' ', '\t', '\n', '\r']) < 0) return trimmed;
        var sb = new System.Text.StringBuilder(trimmed.Length);
        var prevWasSpace = false;
        foreach (var ch in trimmed)
        {
            if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
            {
                if (!prevWasSpace)
                {
                    sb.Append(' ');
                    prevWasSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                prevWasSpace = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Binds an <c>external</c> function declaration to the host's implementation: a function of
    /// the same name and arity registered in the library the query is compiled with. There is
    /// nothing else to bind it to, so an unbound external function is XPST0017 (#18). It used to
    /// get an empty body and silently return (), or fail its return-type check with a message
    /// about the empty sequence rather than the missing implementation.
    /// </summary>
    private bool TryBindExternalFunction(FunctionDeclarationExpression funcDecl, List<AnalysisError> errors)
    {
        var bound = _context.Functions.Resolve(funcDecl.Name, funcDecl.Parameters.Count);
        if (bound is not null and not DeclaredFunctionPlaceholder)
            return true;
        errors.Add(new AnalysisError(XQueryErrorCodes.XPST0017,
            $"External function {funcDecl.Name.LocalName}#{funcDecl.Parameters.Count} has no implementation: "
            + "the host binds one by registering a function of that name and arity in the FunctionLibrary it compiles with",
            funcDecl.Location));
        return false;
    }
}
