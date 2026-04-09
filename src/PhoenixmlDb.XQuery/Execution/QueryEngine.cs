using System.Runtime.CompilerServices;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Analysis;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;
using PhoenixmlDb.XQuery.Parser;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Main query execution engine that orchestrates parsing, analysis, optimization, and execution.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="QueryEngine"/> is the primary entry point for compiling and executing XQuery expressions
/// against PhoenixmlDb storage or any custom data source. Create a single engine instance and reuse it
/// across queries — the engine itself is stateless and thread-safe. Each query execution creates its own
/// <see cref="QueryExecutionContext"/> internally, so concurrent queries do not interfere with each other.
/// </para>
/// <para>
/// The engine supports three execution patterns:
/// <list type="bullet">
///   <item><description><see cref="ExecuteAsync(string, ContainerId, object?, CancellationToken)"/> — streams results one at a time via <c>IAsyncEnumerable</c>, ideal for large result sets.</description></item>
///   <item><description><see cref="ExecuteToListAsync"/> — materializes all results into a list, convenient for small result sets.</description></item>
///   <item><description><see cref="ExecuteScalarAsync"/> — returns only the first result (or <c>null</c>), useful for aggregations and existence checks.</description></item>
/// </list>
/// </para>
/// <para>
/// For advanced scenarios, call <see cref="Compile(string, CompilationOptions?)"/> to pre-compile a query and
/// inspect the <see cref="QueryCompilationResult.ExecutionPlan"/> before executing it.
/// </para>
/// <para>
/// Constructor parameters inject optional providers for node access (<see cref="INodeProvider"/>),
/// document metadata (<see cref="IMetadataProvider"/>), and URI-based document resolution
/// (<see cref="IDocumentResolver"/>). When using the built-in PhoenixmlDb storage layer, these
/// are supplied automatically; implement them only when integrating a custom data source.
/// </para>
/// </remarks>
/// <example>
/// <para>Simple query:</para>
/// <code>
/// var engine = new QueryEngine();
/// await foreach (var item in engine.ExecuteAsync("1 + 2"))
/// {
///     Console.WriteLine(item); // 3
/// }
/// </code>
/// </example>
/// <example>
/// <para>Parameterized query with pre-compilation:</para>
/// <code>
/// var engine = new QueryEngine(nodeProvider: myNodeProvider);
/// var compiled = engine.Compile("//book[price &gt; 30]");
/// if (!compiled.Success)
///     throw new InvalidOperationException(string.Join("; ", compiled.Errors.Select(e => e.Message)));
///
/// var books = await engine.ExecuteToListAsync(compiled.AnalyzedExpression!, myContainer);
/// </code>
/// </example>
/// <example>
/// <para>Cross-document join using a document resolver:</para>
/// <code>
/// var resolver = new MyDocumentResolver();
/// var engine = new QueryEngine(documentResolver: resolver);
///
/// var result = await engine.ExecuteScalarAsync(
///     engine.Compile("count(doc('orders.xml')//order[customer = doc('customers.xml')//customer/@id])").AnalyzedExpression!);
/// </code>
/// </example>
public sealed class QueryEngine
{
    private readonly IQueryPlanOptimizer? _planOptimizer;
    private readonly FunctionLibrary _functions;
    private readonly INodeProvider? _nodeProvider;
    private readonly IMetadataProvider? _metadataProvider;
    private readonly IDocumentResolver? _documentResolver;

    /// <summary>
    /// Creates a new <see cref="QueryEngine"/> with optional providers for node access,
    /// metadata, and document resolution.
    /// </summary>
    /// <param name="planOptimizer">Optional query plan optimizer for database-layer optimizations (e.g. index scans). When <c>null</c>, no external optimizations are applied.</param>
    /// <param name="functions">Custom function library. Defaults to <see cref="FunctionLibrary.Standard"/> which includes all XQuery 3.1 built-in functions.</param>
    /// <param name="nodeProvider">Resolves <see cref="Core.NodeId"/> values to XDM nodes. Required for queries over stored documents.</param>
    /// <param name="metadataProvider">Resolves document-level metadata (e.g., custom properties stored alongside XML).</param>
    /// <param name="documentResolver">Resolves URIs for <c>fn:doc()</c> and <c>fn:collection()</c>. Required for cross-document queries.</param>
    public QueryEngine(
        IQueryPlanOptimizer? planOptimizer = null,
        FunctionLibrary? functions = null,
        INodeProvider? nodeProvider = null,
        IMetadataProvider? metadataProvider = null,
        IDocumentResolver? documentResolver = null)
    {
        _planOptimizer = planOptimizer;
        _functions = functions ?? FunctionLibrary.Standard;
        _nodeProvider = nodeProvider;
        _metadataProvider = metadataProvider;
        _documentResolver = documentResolver;
    }

    /// <summary>
    /// Parses and compiles an XQuery string into an execution plan.
    /// </summary>
    /// <remarks>
    /// Use this method to pre-compile a query for repeated execution or to inspect the
    /// <see cref="QueryCompilationResult.ExecutionPlan"/> for debugging. The returned
    /// <see cref="QueryCompilationResult.Success"/> indicates whether compilation succeeded;
    /// check <see cref="QueryCompilationResult.Errors"/> for diagnostics on failure.
    /// </remarks>
    /// <param name="xquery">The XQuery source text to compile.</param>
    /// <param name="options">Optional compilation settings. When <c>null</c>, defaults are used.</param>
    /// <returns>A compilation result indicating success or failure with diagnostic information.</returns>
    /// <exception cref="XQueryParseException">Thrown if <paramref name="xquery"/> contains syntax errors.</exception>
    /// <seealso cref="Compile(XQueryExpression, CompilationOptions?)"/>
    public QueryCompilationResult Compile(string xquery, CompilationOptions? options = null)
    {
        var parser = new XQueryParserFacade();
        var expression = parser.Parse(xquery);
        return Compile(expression, options);
    }

    /// <summary>
    /// Compiles and executes an XQuery string in one step, streaming results as they are produced.
    /// </summary>
    /// <remarks>
    /// This is the most convenient method for one-off queries. For repeated execution of the same query,
    /// prefer <see cref="Compile(string, CompilationOptions?)"/> followed by
    /// <see cref="ExecuteAsync(ExecutionPlan, ContainerId, object?, CancellationToken)"/> to avoid re-parsing.
    /// </remarks>
    /// <param name="xquery">The XQuery source text.</param>
    /// <param name="container">The container to query against.</param>
    /// <param name="initialContextItem">Optional initial context item (available as <c>.</c> in XQuery). Typically a document node.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>An async sequence of XDM items (nodes, atomic values, or <c>null</c> for the empty sequence).</returns>
    /// <exception cref="XQueryParseException">Thrown if <paramref name="xquery"/> contains syntax errors.</exception>
    /// <exception cref="XQueryRuntimeException">Thrown if the query fails during execution.</exception>
    public IAsyncEnumerable<object?> ExecuteAsync(
        string xquery,
        ContainerId container = default,
        object? initialContextItem = null,
        CancellationToken cancellationToken = default)
    {
        var parser = new XQueryParserFacade();
        var expression = parser.Parse(xquery);
        return ExecuteAsync(expression, container, initialContextItem, cancellationToken);
    }

    /// <summary>
    /// Compiles a pre-parsed query expression into an execution plan.
    /// </summary>
    /// <remarks>
    /// Use this overload when you have already parsed the query via <see cref="XQueryParserFacade"/>
    /// and want to compile it separately — for example, to inspect or transform the AST before compilation.
    /// </remarks>
    /// <param name="expression">A parsed XQuery AST, typically obtained from <see cref="XQueryParserFacade.Parse"/>.</param>
    /// <param name="options">Optional compilation settings.</param>
    /// <returns>A compilation result with the execution plan on success, or errors on failure.</returns>
    public QueryCompilationResult Compile(XQueryExpression expression, CompilationOptions? options = null)
    {
        options ??= new CompilationOptions();
        var errors = new List<AnalysisError>();

        // Phase 1: Static Analysis — clone the function library so imported/declared
        // functions don't leak across compilations
        var staticContext = new StaticContext
        {
            Functions = _functions.Copy(),
            BaseUri = options.BaseUri,
            ExternalModules = options.ExternalModules
        };
        var analyzer = new StaticAnalyzer(staticContext);
        var analysisResult = analyzer.Analyze(expression);

        if (analysisResult.HasErrors)
        {
            return new QueryCompilationResult
            {
                Success = false,
                Errors = analysisResult.Errors.ToList(),
                AnalyzedExpression = analysisResult.Expression
            };
        }

        // Phase 2: Optimization
        var optimizer = new QueryOptimizer(_planOptimizer);
        var optimizationContext = new OptimizationContext
        {
            Container = options.DefaultContainer,
            BoundarySpacePreserve = options.BoundarySpacePreserve,
            StaticContext = staticContext
        };

        var plan = optimizer.Optimize(analysisResult.Expression, optimizationContext);

        return new QueryCompilationResult
        {
            Success = true,
            Errors = errors,
            AnalyzedExpression = analysisResult.Expression,
            ExecutionPlan = plan,
            BaseUri = options.BaseUri
        };
    }

    /// <summary>
    /// Executes a pre-compiled execution plan, streaming results as they are produced.
    /// </summary>
    /// <remarks>
    /// Use this overload for repeated execution of the same query. Obtain the plan from
    /// <see cref="Compile(string, CompilationOptions?)"/> via <see cref="QueryCompilationResult.ExecutionPlan"/>.
    /// A new <see cref="QueryExecutionContext"/> is created for each call, so concurrent executions are safe.
    /// </remarks>
    /// <param name="plan">The pre-compiled execution plan.</param>
    /// <param name="container">The container to query against.</param>
    /// <param name="initialContextItem">Optional initial context item (available as <c>.</c> in XQuery). Typically a document node.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>An async sequence of XDM items.</returns>
    /// <exception cref="XQueryRuntimeException">Thrown if the query fails during execution.</exception>
    public async IAsyncEnumerable<object?> ExecuteAsync(
        ExecutionPlan plan,
        ContainerId container = default,
        object? initialContextItem = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Derive namespace resolver from node provider if it supports it
        Func<NamespaceId, string?>? nsResolver = _nodeProvider is INodeStore nodeStore
            ? id => nodeStore.GetNamespaceUri(id)
            : null;

        using var context = new QueryExecutionContext(
            container,
            _functions.Copy(),
            _nodeProvider,
            _metadataProvider,
            _documentResolver,
            namespaceResolver: nsResolver,
            cancellationToken: cancellationToken);

        if (initialContextItem != null)
        {
            context.PushContextItem(initialContextItem);
        }

        await foreach (var item in plan.ExecuteAsync(context))
        {
            yield return item;
        }

        // Apply any pending updates collected by update expressions (insert/delete/replace/rename).
        // TransformOperator handles its own PUL internally; this covers top-level update expressions.
        if (context.PendingUpdates.HasUpdates)
        {
            if (_nodeProvider is IUpdatableNodeStore updatableStore)
            {
                PendingUpdateApplicator.Apply(context.PendingUpdates, updatableStore);
            }
            else
            {
                throw new XQueryRuntimeException("XUST0002",
                    "Update expressions require an IUpdatableNodeStore-capable node provider. " +
                    "Use XdmDocumentStore with update support, or use copy/modify/return (transform) expressions.");
            }
        }
    }

    /// <summary>
    /// Compiles and executes a pre-parsed query expression in one step, streaming results.
    /// </summary>
    /// <param name="expression">A parsed XQuery AST.</param>
    /// <param name="container">The container to query against.</param>
    /// <param name="initialContextItem">Optional initial context item (available as <c>.</c> in XQuery). Typically a document node.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>An async sequence of XDM items.</returns>
    /// <exception cref="XQueryRuntimeException">Thrown if compilation or execution fails.</exception>
    public async IAsyncEnumerable<object?> ExecuteAsync(
        XQueryExpression expression,
        ContainerId container = default,
        object? initialContextItem = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var compilationResult = Compile(expression, new CompilationOptions
        {
            DefaultContainer = container
        });

        if (!compilationResult.Success)
        {
            var errorMessages = string.Join("; ", compilationResult.Errors.Select(e => e.Message));
            var firstCode = compilationResult.Errors.FirstOrDefault()?.Code ?? "XPST0003";
            throw new XQueryRuntimeException(firstCode, $"Compilation failed: {errorMessages}");
        }

        await foreach (var item in ExecuteAsync(
            compilationResult.ExecutionPlan!,
            container,
            initialContextItem,
            cancellationToken))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Executes a query and materializes all results into an in-memory list.
    /// </summary>
    /// <remarks>
    /// Convenience method for queries with bounded result sets. For large or unbounded results,
    /// prefer <see cref="ExecuteAsync(XQueryExpression, ContainerId, object?, CancellationToken)"/> to avoid
    /// excessive memory consumption. The materialization limit configured in
    /// <see cref="QueryExecutionLimits.MaxResultItems"/> applies.
    /// </remarks>
    /// <param name="expression">A parsed XQuery AST.</param>
    /// <param name="container">The container to query against.</param>
    /// <param name="initialContextItem">Optional initial context item (available as <c>.</c> in XQuery). Typically a document node.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>A read-only list of all query results.</returns>
    /// <exception cref="XQueryRuntimeException">Thrown if the query fails or exceeds materialization limits.</exception>
    public async Task<IReadOnlyList<object?>> ExecuteToListAsync(
        XQueryExpression expression,
        ContainerId container = default,
        object? initialContextItem = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<object?>();
        await foreach (var item in ExecuteAsync(expression, container, initialContextItem, cancellationToken))
        {
            results.Add(item);
        }
        return results;
    }

    /// <summary>
    /// Executes a query and returns only the first result, or <c>null</c> if the result is the empty sequence.
    /// </summary>
    /// <remarks>
    /// Ideal for queries that return a single value, such as <c>count(//book)</c> or
    /// <c>//employee[@id = 42]/name</c>. The query is short-circuited after the first item is produced.
    /// </remarks>
    /// <param name="expression">A parsed XQuery AST.</param>
    /// <param name="container">The container to query against.</param>
    /// <param name="initialContextItem">Optional initial context item (available as <c>.</c> in XQuery). Typically a document node.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The first item of the result sequence, or <c>null</c> if the sequence is empty.</returns>
    /// <exception cref="XQueryRuntimeException">Thrown if the query fails during execution.</exception>
    public async Task<object?> ExecuteScalarAsync(
        XQueryExpression expression,
        ContainerId container = default,
        object? initialContextItem = null,
        CancellationToken cancellationToken = default)
    {
        await foreach (var item in ExecuteAsync(expression, container, initialContextItem, cancellationToken))
        {
            return item;
        }
        return null;
    }

    /// <summary>
    /// Creates a standalone execution context for manual query execution.
    /// </summary>
    /// <remarks>
    /// Most callers should use <see cref="ExecuteAsync(string, ContainerId, object?, CancellationToken)"/> or its
    /// overloads instead. This method is exposed for advanced scenarios such as binding external variables
    /// or controlling scope lifetime explicitly. The caller is responsible for disposing the returned context.
    /// </remarks>
    /// <param name="container">The container to query against.</param>
    /// <param name="initialContextItem">Optional initial context item (available as <c>.</c> in XQuery). Typically a document node.</param>
    /// <param name="staticBaseUri">Static base URI for <c>fn:static-base-uri()</c> and relative URI resolution. Typically from <see cref="QueryCompilationResult.BaseUri"/>.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>A new per-query execution context. Must be disposed after use.</returns>
    public QueryExecutionContext CreateContext(
        ContainerId container = default,
        object? initialContextItem = null,
        string? staticBaseUri = null,
        CancellationToken cancellationToken = default)
    {
        // Derive namespace resolver from node provider if it supports it
        Func<NamespaceId, string?>? nsResolver = _nodeProvider is INodeStore nodeStore
            ? id => nodeStore.GetNamespaceUri(id)
            : null;

        var context = new QueryExecutionContext(
            container,
            _functions.Copy(),
            _nodeProvider,
            _metadataProvider,
            _documentResolver,
            namespaceResolver: nsResolver,
            cancellationToken: cancellationToken);

        if (staticBaseUri != null)
            context.StaticBaseUri = staticBaseUri;

        if (initialContextItem != null)
        {
            context.PushContextItem(initialContextItem);
        }

        return context;
    }
}

/// <summary>
/// Controls how an XQuery expression is compiled by <see cref="QueryEngine.Compile(string, CompilationOptions?)"/>.
/// </summary>
/// <remarks>
/// All properties have sensible defaults. In most cases you only need to set
/// <see cref="DefaultContainer"/> to target a specific data container.
/// </remarks>
/// <example>
/// <code>
/// var options = new CompilationOptions
/// {
///     DefaultContainer = myContainer,
///     StrictTypeChecking = true
/// };
/// var result = engine.Compile("//book[price &gt; 30]", options);
/// </code>
/// </example>
public sealed class CompilationOptions
{
    /// <summary>
    /// The default container that unqualified path expressions resolve against.
    /// </summary>
    public ContainerId DefaultContainer { get; init; }

    /// <summary>
    /// Whether to apply query optimizations such as constant folding, predicate simplification,
    /// and index selection. Defaults to <c>true</c>. Disable for debugging or to inspect the
    /// unoptimized execution plan.
    /// </summary>
    public bool EnableOptimization { get; init; } = true;

    /// <summary>
    /// Whether to enforce strict XQuery 3.1 static type checking. When <c>false</c> (the default),
    /// the engine uses optimistic typing and defers most type errors to runtime.
    /// </summary>
    public bool StrictTypeChecking { get; init; } = false;

    /// <summary>
    /// When true, boundary whitespace in direct element constructors is preserved
    /// (corresponds to 'declare boundary-space preserve'). Default is false (strip).
    /// </summary>
    public bool BoundarySpacePreserve { get; init; } = false;

    /// <summary>
    /// Base URI for resolving relative module location hints in <c>import module</c> declarations.
    /// Typically the URI of the query file itself.
    /// </summary>
    public string? BaseUri { get; init; }

    /// <summary>
    /// External module registry: maps module namespace URI to a file path containing the module
    /// source. Consulted by the static analyzer when an <c>import module</c> declaration cannot be
    /// resolved from its location hints (or has none). Allows hosts (e.g. test runners) to wire up
    /// module imports without requiring location hints in the query text.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExternalModules { get; init; }
}

/// <summary>
/// The result of compiling an XQuery expression via <see cref="QueryEngine.Compile(string, CompilationOptions?)"/>.
/// </summary>
/// <remarks>
/// Always check <see cref="Success"/> before accessing <see cref="ExecutionPlan"/>. On failure,
/// <see cref="Errors"/> contains the list of static analysis errors. On success, both
/// <see cref="AnalyzedExpression"/> and <see cref="ExecutionPlan"/> are populated and can be
/// passed to <see cref="QueryEngine.ExecuteAsync(ExecutionPlan, ContainerId, object?, CancellationToken)"/>.
/// </remarks>
/// <example>
/// <code>
/// var result = engine.Compile("//book[price &gt; 30]");
/// if (result.Success)
/// {
///     Console.WriteLine($"Plan: {result.ExecutionPlan}");
///     await foreach (var item in engine.ExecuteAsync(result.ExecutionPlan!))
///         Console.WriteLine(item);
/// }
/// else
/// {
///     foreach (var error in result.Errors)
///         Console.Error.WriteLine(error.Message);
/// }
/// </code>
/// </example>
public sealed class QueryCompilationResult
{
    /// <summary>
    /// Whether compilation succeeded without errors.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Static analysis errors encountered during compilation. Empty when <see cref="Success"/> is <c>true</c>.
    /// </summary>
    public required IReadOnlyList<AnalysisError> Errors { get; init; }

    /// <summary>
    /// The analyzed expression with resolved names, inferred types, and bound variables.
    /// Available on both success and failure (on failure, the expression may be partially analyzed).
    /// </summary>
    public XQueryExpression? AnalyzedExpression { get; init; }

    /// <summary>
    /// The optimized execution plan, ready for execution via
    /// <see cref="QueryEngine.ExecuteAsync(ExecutionPlan, ContainerId, object?, CancellationToken)"/>.
    /// Only populated when <see cref="Success"/> is <c>true</c>.
    /// </summary>
    public ExecutionPlan? ExecutionPlan { get; init; }

    /// <summary>
    /// The static base URI from compilation options. Set on the execution context
    /// so <c>fn:static-base-uri()</c> and <c>fn:resolve-uri()</c> work correctly.
    /// </summary>
    public string? BaseUri { get; init; }
}
