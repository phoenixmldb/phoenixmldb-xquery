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
    private readonly HashSet<string> _resolvedModuleUris = new();

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
        // Cycle detection: skip modules we've already resolved to prevent infinite recursion
        // when module A imports B which imports A (or deeper cycles).
        if (!_resolvedModuleUris.Add(modImport.NamespaceUri))
            return true; // Already resolved this module

        // Build the candidate file path list from multiple sources:
        // 1. Resolve explicit location hints via ExternalModuleLocations mapping
        // 2. Try resolving location hints as file paths
        // 3. Fall back to ExternalModules registry (namespace URI → file paths)
        var resolvedPaths = new List<string>();

        // First, try resolving each location hint
        foreach (var hint in modImport.LocationHints)
        {
            string? modulePath = null;

            // Check location hint mapping (e.g. http:// URI → actual file path)
            if (_context.ExternalModuleLocations != null
                && _context.ExternalModuleLocations.TryGetValue(hint, out var mappedPath))
            {
                modulePath = mappedPath;
            }
            else if (Uri.TryCreate(hint, UriKind.Absolute, out var absUri) && absUri.IsFile)
            {
                modulePath = absUri.LocalPath;
            }
            else if (_context.BaseUri != null)
            {
                if (Uri.TryCreate(_context.BaseUri, UriKind.Absolute, out var baseUri))
                {
                    if (Uri.TryCreate(baseUri, hint, out var resolved) && resolved.IsFile)
                        modulePath = resolved.LocalPath;
                }
            }

            modulePath ??= hint;
            if (TryFindFile(modulePath, out var foundPath))
                resolvedPaths.Add(foundPath);
        }

        // Fall back to ExternalModules registry (namespace URI → file paths)
        if (resolvedPaths.Count == 0 && _context.ExternalModules != null
            && _context.ExternalModules.TryGetValue(modImport.NamespaceUri, out var registeredPaths))
        {
            resolvedPaths.AddRange(registeredPaths);
        }

        if (resolvedPaths.Count == 0)
            return false;

        bool anyLoaded = false;
        foreach (var modulePath in resolvedPaths)
        {
            if (TryLoadModuleFile(modulePath, modImport, errors))
                anyLoaded = true;
        }

        return anyLoaded;
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
    private bool TryLoadModuleFile(string modulePath, ModuleImportExpression modImport, List<AnalysisError> errors)
    {
        try
        {
            var moduleSource = System.IO.File.ReadAllText(modulePath);
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

                    case ModuleImportExpression nestedModImport:
                        // Register the namespace prefix from nested import
                        if (nestedModImport.Prefix != null)
                            _context.Namespaces.RegisterNamespace(nestedModImport.Prefix, nestedModImport.NamespaceUri);
                        // Recursively resolve nested module imports
                        TryResolveModule(nestedModImport, errors);
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
            || uri == "http://www.w3.org/2005/xpath-functions/array";
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
    private XQueryExpression InjectImportedDeclarations(XQueryExpression expression)
    {
        if (expression is not ModuleExpression module || _context.ImportedModules.Count == 0)
            return expression;

        // Imported module declarations must come BEFORE the main module's declarations
        // so that imported variables are bound before main module variables that may
        // reference imported functions (which in turn reference imported variables).
        var importedDecls = new List<XQueryExpression>();
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

            foreach (var decl in importedModule.Declarations)
            {
                if (decl is FunctionDeclarationExpression funcDecl)
                {
                    // Use the resolved name (with correct NamespaceId)
                    var resolvedName = ResolveQName(funcDecl.Name);
                    if (resolvedName != funcDecl.Name)
                    {
                        importedDecls.Add(new FunctionDeclarationExpression
                        {
                            Name = resolvedName,
                            Parameters = funcDecl.Parameters,
                            ReturnType = funcDecl.ReturnType,
                            Body = funcDecl.Body,
                            IsPrivate = funcDecl.IsPrivate,
                            Location = funcDecl.Location,
                            ModuleBaseUri = moduleBaseUri
                        });
                    }
                    else
                    {
                        if (moduleBaseUri != null && funcDecl.ModuleBaseUri == null)
                            funcDecl.ModuleBaseUri = moduleBaseUri;
                        importedDecls.Add(funcDecl);
                    }
                }
                else if (decl is VariableDeclarationExpression varDecl)
                {
                    // Resolve the variable name to the correct NamespaceId
                    var resolvedVarName = ResolveQName(varDecl.Name);
                    if (resolvedVarName != varDecl.Name)
                        varDecl.Name = resolvedVarName;
                    importedDecls.Add(varDecl);
                }
            }

            // Restore namespace context — module internal prefixes must not leak
            _context.Namespaces.RestorePrefixes(savedNs);
        }

        // Prepend imported declarations before main module declarations
        var augmented = new List<XQueryExpression>(importedDecls.Count + module.Declarations.Count);
        augmented.AddRange(importedDecls);
        augmented.AddRange(module.Declarations);

        return new ModuleExpression
        {
            Declarations = augmented,
            Body = module.Body,
            Location = module.Location,
            BaseUri = module.BaseUri
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

                    // Resolve and load the library module from location hints
                    if (!TryResolveModule(effectiveImport, errors))
                    {
                        errors.Add(new AnalysisError(
                            XQueryErrorCodes.XQST0059,
                            $"Module '{normalizedUri}' could not be resolved from location hints: [{string.Join(", ", modImport.LocationHints)}]",
                            modImport.Location));
                    }
                    break;
            }
        }

        // Second pass: register functions and variables (after namespaces are set up)
        // Track what functions/variables were imported to detect XQST0034/XQST0049 collisions
        var importedFunctions = new HashSet<string>(); // "ns:local#arity"
        var importedVariables = new HashSet<string>(); // "ns:local"

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
                    var fnUri = funcDecl.Name.Prefix != null
                        ? _context.Namespaces.ResolvePrefix(funcDecl.Name.Prefix)
                        : (_context.Namespaces.ResolvePrefix("##default-function") ?? _context.DefaultFunctionNamespace);
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

                    // XQST0049: variable name collides with imported variable of same name
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
}

/// <summary>
/// Placeholder function registered during static analysis for user-defined functions.
/// Allows FunctionResolver to recognize calls to prolog-declared functions.
/// </summary>
internal sealed class DeclaredFunctionPlaceholder : XQueryFunction
{
    private readonly FunctionDeclarationExpression _decl;

    public DeclaredFunctionPlaceholder(FunctionDeclarationExpression decl, bool isFromImportedModule = false)
    {
        _decl = decl;
        IsFromImportedModule = isFromImportedModule;
        IsModulePrivate = isFromImportedModule && decl.IsPrivate;
    }

    /// <summary>
    /// True when this function was registered from an imported module (used for XQST0034 collision detection).
    /// </summary>
    public bool IsFromImportedModule { get; }

    public override QName Name => _decl.Name;
    public override XdmSequenceType ReturnType => _decl.ReturnType ?? XdmSequenceType.ZeroOrMoreItems;

    /// <summary>
    /// True when this function was declared with <c>%private</c> in an imported module.
    /// Private functions are accessible within the module but not from importing modules.
    /// Main module %private functions are always accessible.
    /// </summary>
    public bool IsModulePrivate { get; }

    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        _decl.Parameters.Select(p => new FunctionParameterDef
        {
            Name = p.Name,
            Type = p.Type ?? XdmSequenceType.ZeroOrMoreItems
        }).ToList();

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // This placeholder should never be invoked at runtime;
        // the actual DeclaredFunction from FunctionDeclarationOperator replaces it.
        throw new InvalidOperationException(
            $"Placeholder for declared function {Name.LocalName} invoked at runtime");
    }
}

/// <summary>
/// Result of static analysis.
/// </summary>
public sealed record AnalysisResult(
    XQueryExpression Expression,
    IReadOnlyList<AnalysisError> Errors)
{
    public bool HasErrors => Errors.Any(e => e.Severity == AnalysisErrorSeverity.Error);
    public bool HasWarnings => Errors.Any(e => e.Severity == AnalysisErrorSeverity.Warning);
}

/// <summary>
/// An error or warning from static analysis.
/// </summary>
public sealed record AnalysisError(
    string Code,
    string Message,
    SourceLocation? Location,
    AnalysisErrorSeverity Severity = AnalysisErrorSeverity.Error);

/// <summary>
/// Severity of an analysis error.
/// </summary>
public enum AnalysisErrorSeverity
{
    Warning,
    Error
}

/// <summary>
/// Standard XQuery error codes.
/// </summary>
public static class XQueryErrorCodes
{
    // Static errors
    public const string XPST0003 = "XPST0003"; // Static syntax error
    public const string XPST0008 = "XPST0008"; // Undefined variable
    public const string XPST0017 = "XPST0017"; // Undefined function
    public const string XPST0051 = "XPST0051"; // Unknown atomic type
    public const string XPST0081 = "XPST0081"; // Unbound prefix
    public const string XQST0059 = "XQST0059"; // Module not found
    public const string XQST0070 = "XQST0070"; // Reserved namespace prefix
    public const string XQST0071 = "XQST0071"; // Duplicate namespace prefix
    public const string XQST0085 = "XQST0085"; // Empty namespace URI with non-empty prefix
    public const string XQST0066 = "XQST0066"; // Duplicate default namespace declaration
    public const string XQST0033 = "XQST0033"; // Duplicate namespace prefix declaration
    public const string XQST0034 = "XQST0034"; // Duplicate function declaration
    public const string XQST0045 = "XQST0045"; // Function declared in reserved namespace
    public const string XQST0047 = "XQST0047"; // Duplicate module target namespace import
    public const string XQST0048 = "XQST0048"; // Variable/function not in module namespace
    public const string XQST0049 = "XQST0049"; // Duplicate variable declaration
    public const string XQST0088 = "XQST0088"; // Empty module namespace URI

    // Type errors
    public const string XPTY0004 = "XPTY0004"; // Type mismatch
    public const string XPTY0018 = "XPTY0018"; // Path step returns mixed nodes and atomics
    public const string XPTY0019 = "XPTY0019"; // Context item is not a node
    public const string XPTY0020 = "XPTY0020"; // Context item is undefined

    // Dynamic errors
    public const string XQDY0025 = "XQDY0025"; // Duplicate attribute
    public const string XQDY0041 = "XQDY0041"; // Cast error
    public const string XQDY0044 = "XQDY0044"; // Invalid attribute name
    public const string XQDY0064 = "XQDY0064"; // Invalid PI target
    public const string XQDY0072 = "XQDY0072"; // Comment contains --
    public const string XQDY0074 = "XQDY0074"; // Invalid element name
    public const string XQDY0084 = "XQDY0084"; // Element content error
    public const string XQDY0091 = "XQDY0091"; // Invalid namespace prefix
    public const string XQDY0096 = "XQDY0096"; // Invalid namespace URI

    // Serialization errors
    public const string SEPM0004 = "SEPM0004"; // Unsupported parameter
    public const string SEPM0009 = "SEPM0009"; // Omit-xml-declaration incompatible
    public const string SEPM0010 = "SEPM0010"; // Invalid output method

    // Function errors
    public const string FOAR0001 = "FOAR0001"; // Division by zero
    public const string FOAR0002 = "FOAR0002"; // Overflow/underflow
    public const string FOCA0002 = "FOCA0002"; // Invalid lexical value
    public const string FOCH0001 = "FOCH0001"; // Code point not valid
    public const string FOCH0002 = "FOCH0002"; // Unsupported collation
    public const string FODC0001 = "FODC0001"; // No context document
    public const string FODC0002 = "FODC0002"; // Error retrieving resource
    public const string FODC0004 = "FODC0004"; // Invalid collection URI
    public const string FODC0005 = "FODC0005"; // Invalid document URI
    public const string FODT0001 = "FODT0001"; // Overflow in date/time
    public const string FODT0002 = "FODT0002"; // Overflow in duration
    public const string FOER0000 = "FOER0000"; // Unidentified error
    public const string FONS0004 = "FONS0004"; // No namespace for prefix
    public const string FORG0001 = "FORG0001"; // Invalid value
    public const string FORG0006 = "FORG0006"; // Invalid argument type
    public const string FORX0001 = "FORX0001"; // Invalid regex flags
    public const string FORX0002 = "FORX0002"; // Invalid regex
    public const string FORX0003 = "FORX0003"; // Regex matches zero-length
    public const string FORX0004 = "FORX0004"; // Invalid replacement string
    public const string FOTY0012 = "FOTY0012"; // Argument is a function
    public const string FOTY0013 = "FOTY0013"; // Atomization of function
    public const string FOTY0014 = "FOTY0014"; // Numeric operation on duration
    public const string FOTY0015 = "FOTY0015"; // Deep-equal on function
}
