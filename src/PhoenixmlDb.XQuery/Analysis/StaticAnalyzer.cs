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
        var funcResolver = new FunctionResolver(_context.Functions, _context.Namespaces);
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
        if (modImport.LocationHints.Count == 0)
            return false;

        foreach (var hint in modImport.LocationHints)
        {
            string? modulePath = null;
#pragma warning disable CA1849

            if (Uri.TryCreate(hint, UriKind.Absolute, out var absUri) && absUri.IsFile)
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
            if (!System.IO.File.Exists(modulePath))
            {
                var fullPath = System.IO.Path.GetFullPath(modulePath);
                if (System.IO.File.Exists(fullPath))
                    modulePath = fullPath;
                else
                    continue;
            }

            try
            {
                var moduleSource = System.IO.File.ReadAllText(modulePath);
                var parser = new Parser.XQueryParserFacade();
                var moduleAst = parser.Parse(moduleSource);

                if (moduleAst is not ModuleExpression moduleExpr)
                    continue;

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
                        _context.Namespaces.RegisterNamespace(nsDecl.Prefix, nsDecl.Uri);
                    }
                }

                // Second pass: register functions and variables with resolved names
                foreach (var decl in moduleExpr.Declarations)
                {
                    switch (decl)
                    {
                        case FunctionDeclarationExpression funcDecl:
                            var resolvedName = ResolveQName(funcDecl.Name);
                            var resolvedDecl = resolvedName != funcDecl.Name
                                ? new FunctionDeclarationExpression
                                {
                                    Name = resolvedName,
                                    Parameters = funcDecl.Parameters,
                                    ReturnType = funcDecl.ReturnType,
                                    Body = funcDecl.Body,
                                    Location = funcDecl.Location
                                }
                                : funcDecl;
                            _context.Functions.Register(new DeclaredFunctionPlaceholder(resolvedDecl));
                            break;

                        case VariableDeclarationExpression varDecl:
                            _context.RegisterGlobalVariable(varDecl.Name, varDecl.TypeDeclaration);
                            break;
                    }
                }

                _context.ImportedModules[modImport.NamespaceUri] = moduleExpr;
                return true;
            }
            catch (Exception ex)
            {
                errors.Add(new AnalysisError(
                    XQueryErrorCodes.XQST0059,
                    $"Error loading module from '{modulePath}': {ex.Message}",
                    modImport.Location));
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a QName's prefix to a namespace ID using the static context.
    /// </summary>
    private QName ResolveQName(QName name)
    {
        if (name.Prefix == null) return name;
        var uri = _context.Namespaces.ResolvePrefix(name.Prefix);
        if (uri == null) return name;
        var nsId = _context.Namespaces.GetOrCreateId(uri);
        return new QName(nsId, name.LocalName, name.Prefix);
    }

    /// <summary>
    /// Injects function and variable declarations from imported modules into the main module's
    /// declarations list so the optimizer creates physical operators for them.
    /// </summary>
    private XQueryExpression InjectImportedDeclarations(XQueryExpression expression)
    {
        if (expression is not ModuleExpression module || _context.ImportedModules.Count == 0)
            return expression;

        var augmented = new List<XQueryExpression>(module.Declarations);
        foreach (var (_, importedModule) in _context.ImportedModules)
        {
            foreach (var decl in importedModule.Declarations)
            {
                if (decl is FunctionDeclarationExpression funcDecl)
                {
                    // Use the resolved name (with correct NamespaceId)
                    var resolvedName = ResolveQName(funcDecl.Name);
                    if (resolvedName != funcDecl.Name)
                    {
                        augmented.Add(new FunctionDeclarationExpression
                        {
                            Name = resolvedName,
                            Parameters = funcDecl.Parameters,
                            ReturnType = funcDecl.ReturnType,
                            Body = funcDecl.Body,
                            Location = funcDecl.Location
                        });
                    }
                    else
                    {
                        augmented.Add(funcDecl);
                    }
                }
                else if (decl is VariableDeclarationExpression)
                {
                    augmented.Add(decl);
                }
            }
        }

        return new ModuleExpression
        {
            Declarations = augmented,
            Body = module.Body,
            Location = module.Location
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
                    }
                    _context.Namespaces.RegisterNamespace(nsDecl.Prefix, nsDecl.Uri);
                    break;

                case ModuleImportExpression modImport:
                    // Register the namespace prefix binding so downstream analysis can resolve it
                    if (modImport.Prefix != null)
                        _context.Namespaces.RegisterNamespace(modImport.Prefix, modImport.NamespaceUri);

                    // Resolve and load the library module from location hints
                    if (!TryResolveModule(modImport, errors))
                    {
                        errors.Add(new AnalysisError(
                            XQueryErrorCodes.XQST0059,
                            $"Module '{modImport.NamespaceUri}' could not be resolved from location hints: [{string.Join(", ", modImport.LocationHints)}]",
                            modImport.Location));
                    }
                    break;
            }
        }

        // Second pass: register functions and variables (after namespaces are set up)
        foreach (var decl in module.Declarations)
        {
            switch (decl)
            {
                case FunctionDeclarationExpression funcDecl:
                    // Resolve namespace prefix on function name
                    var resolvedName = ResolveQName(funcDecl.Name);
                    var resolvedDecl = resolvedName != funcDecl.Name
                        ? new FunctionDeclarationExpression
                        {
                            Name = resolvedName,
                            Parameters = funcDecl.Parameters,
                            ReturnType = funcDecl.ReturnType,
                            Body = funcDecl.Body,
                            Location = funcDecl.Location
                        }
                        : funcDecl;

                    var placeholder = new DeclaredFunctionPlaceholder(resolvedDecl);
                    _context.Functions.Register(placeholder);
                    break;

                case VariableDeclarationExpression varDecl:
                    _context.RegisterGlobalVariable(varDecl.Name, varDecl.TypeDeclaration);
                    break;
            }
        }
    }
}

/// <summary>
/// Placeholder function registered during static analysis for user-defined functions.
/// Allows FunctionResolver to recognize calls to prolog-declared functions.
/// </summary>
internal sealed class DeclaredFunctionPlaceholder : XQueryFunction
{
    private readonly FunctionDeclarationExpression _decl;

    public DeclaredFunctionPlaceholder(FunctionDeclarationExpression decl)
    {
        _decl = decl;
    }

    public override QName Name => _decl.Name;
    public override XdmSequenceType ReturnType => _decl.ReturnType ?? XdmSequenceType.ZeroOrMoreItems;

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
