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
public sealed class QueryEngine
{
    private readonly IIndexConfiguration? _indexConfig;
    private readonly FunctionLibrary _functions;
    private readonly INodeProvider? _nodeProvider;
    private readonly IMetadataProvider? _metadataProvider;
    private readonly IDocumentResolver? _documentResolver;

    public QueryEngine(
        IIndexConfiguration? indexConfig = null,
        FunctionLibrary? functions = null,
        INodeProvider? nodeProvider = null,
        IMetadataProvider? metadataProvider = null,
        IDocumentResolver? documentResolver = null)
    {
        _indexConfig = indexConfig;
        _functions = functions ?? FunctionLibrary.Standard;
        _nodeProvider = nodeProvider;
        _metadataProvider = metadataProvider;
        _documentResolver = documentResolver;
    }

    /// <summary>
    /// Parses and compiles an XQuery string into an execution plan.
    /// </summary>
    public QueryCompilationResult Compile(string xquery, CompilationOptions? options = null)
    {
        var parser = new XQueryParserFacade();
        var expression = parser.Parse(xquery);
        return Compile(expression, options);
    }

    /// <summary>
    /// Compiles and executes an XQuery string in one step.
    /// </summary>
    public IAsyncEnumerable<object?> ExecuteAsync(
        string xquery,
        ContainerId container = default,
        CancellationToken cancellationToken = default)
    {
        var parser = new XQueryParserFacade();
        var expression = parser.Parse(xquery);
        return ExecuteAsync(expression, container, cancellationToken);
    }

    /// <summary>
    /// Compiles a query expression into an execution plan.
    /// </summary>
    public QueryCompilationResult Compile(XQueryExpression expression, CompilationOptions? options = null)
    {
        options ??= new CompilationOptions();
        var errors = new List<AnalysisError>();

        // Phase 1: Static Analysis
        var staticContext = new StaticContext
        {
            Functions = _functions
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
        var optimizer = new QueryOptimizer(_indexConfig);
        var optimizationContext = new OptimizationContext
        {
            Container = options.DefaultContainer
        };

        var plan = optimizer.Optimize(analysisResult.Expression, optimizationContext);

        return new QueryCompilationResult
        {
            Success = true,
            Errors = errors,
            AnalyzedExpression = analysisResult.Expression,
            ExecutionPlan = plan
        };
    }

    /// <summary>
    /// Executes a pre-compiled execution plan.
    /// </summary>
    public async IAsyncEnumerable<object?> ExecuteAsync(
        ExecutionPlan plan,
        ContainerId container = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var context = new QueryExecutionContext(
            container,
            _functions,
            _nodeProvider,
            _metadataProvider,
            _documentResolver,
            cancellationToken: cancellationToken);

        await foreach (var item in plan.ExecuteAsync(context))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Compiles and executes a query expression in one step.
    /// </summary>
    public async IAsyncEnumerable<object?> ExecuteAsync(
        XQueryExpression expression,
        ContainerId container = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var compilationResult = Compile(expression, new CompilationOptions
        {
            DefaultContainer = container
        });

        if (!compilationResult.Success)
        {
            var errorMessages = string.Join("; ", compilationResult.Errors.Select(e => e.Message));
            throw new XQueryRuntimeException("XPST0003", $"Compilation failed: {errorMessages}");
        }

        await foreach (var item in ExecuteAsync(
            compilationResult.ExecutionPlan!,
            container,
            cancellationToken))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Executes a query and returns results as a list.
    /// </summary>
    public async Task<IReadOnlyList<object?>> ExecuteToListAsync(
        XQueryExpression expression,
        ContainerId container = default,
        CancellationToken cancellationToken = default)
    {
        var results = new List<object?>();
        await foreach (var item in ExecuteAsync(expression, container, cancellationToken))
        {
            results.Add(item);
        }
        return results;
    }

    /// <summary>
    /// Executes a query and returns the first result or null.
    /// </summary>
    public async Task<object?> ExecuteScalarAsync(
        XQueryExpression expression,
        ContainerId container = default,
        CancellationToken cancellationToken = default)
    {
        await foreach (var item in ExecuteAsync(expression, container, cancellationToken))
        {
            return item;
        }
        return null;
    }

    /// <summary>
    /// Creates an execution context for manual query execution.
    /// </summary>
    public QueryExecutionContext CreateContext(
        ContainerId container = default,
        CancellationToken cancellationToken = default)
    {
        return new QueryExecutionContext(
            container,
            _functions,
            _nodeProvider,
            _metadataProvider,
            _documentResolver,
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Options for query compilation.
/// </summary>
public sealed class CompilationOptions
{
    /// <summary>
    /// Default container for the query.
    /// </summary>
    public ContainerId DefaultContainer { get; init; }

    /// <summary>
    /// Whether to enable optimization.
    /// </summary>
    public bool EnableOptimization { get; init; } = true;

    /// <summary>
    /// Whether to enable strict type checking.
    /// </summary>
    public bool StrictTypeChecking { get; init; } = false;
}

/// <summary>
/// Result of query compilation.
/// </summary>
public sealed class QueryCompilationResult
{
    /// <summary>
    /// Whether compilation succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Compilation errors.
    /// </summary>
    public required IReadOnlyList<AnalysisError> Errors { get; init; }

    /// <summary>
    /// The analyzed expression (with resolved names, types, etc.).
    /// </summary>
    public XQueryExpression? AnalyzedExpression { get; init; }

    /// <summary>
    /// The execution plan (if compilation succeeded).
    /// </summary>
    public ExecutionPlan? ExecutionPlan { get; init; }
}
