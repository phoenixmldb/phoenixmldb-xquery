using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Cli;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

var options = CliOptions.Parse(args);

if (options.ShowVersion)
{
    var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    Console.WriteLine($"xquery {version} (PhoenixmlDb XQuery/XPath 4.0)");
    return 0;
}

if (options.ShowHelp || (options.Query == null && options.QueryFile == null))
{
    PrintUsage();
    return options.ShowHelp ? 0 : 1;
}

try
{
    var totalSw = Stopwatch.StartNew();

    // Resolve the query text
    string query;
    if (options.QueryFile != null)
    {
        if (!File.Exists(options.QueryFile))
        {
            await Console.Error.WriteLineAsync($"Error: Query file not found: {options.QueryFile}").ConfigureAwait(true);
            return 1;
        }
        query = await File.ReadAllTextAsync(options.QueryFile).ConfigureAwait(true);
    }
    else
    {
        query = options.Query!;
    }

    // If no explicit -o flag was given, check the query prolog for declare option output:method
    var outputMethod = options.OutputMethod;
    if (!options.OutputMethodExplicit)
    {
        const string optionPattern = @"declare\s+option\s+(?:output:(\w[\w-]*)|Q\{[^}]*\}(\w[\w-]*))\s+[""']([^""']*)[""']";
        foreach (Match match in Regex.Matches(query, optionPattern, RegexOptions.IgnoreCase))
        {
            var optionName = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
                .ToLowerInvariant();
            if (optionName == "method")
            {
                outputMethod = match.Groups[3].Value.ToLowerInvariant() switch
                {
                    "json" => OutputMethod.Json,
                    "xml" => OutputMethod.Xml,
                    "text" => OutputMethod.Text,
                    _ => OutputMethod.Adaptive
                };
                break;
            }
        }
    }

    // Set up the document environment
    var loadSw = Stopwatch.StartNew();
    var env = new DocumentEnvironment();
    XdmDocument? contextDocument = null;

    // Load input sources
    foreach (var source in options.Sources)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            var doc = env.LoadFromUrl(source);
            contextDocument ??= doc;
        }
        else if (Directory.Exists(source))
        {
            var docs = env.LoadDirectory(source);
            if (docs.Count > 0)
                contextDocument ??= docs[0];
        }
        else if (File.Exists(source))
        {
            var doc = env.LoadFile(source);
            contextDocument ??= doc;
        }
        else
        {
            await Console.Error.WriteLineAsync($"Warning: Source not found: {source}").ConfigureAwait(true);
        }
    }

    // Read from stdin if explicitly requested or if data is available on a pipe
    if (options.Sources.Count == 0 && options.ReadStdin)
    {
        var stdin = Console.OpenStandardInput();
        var buf = new byte[1];
        // Peek first byte with timeout to detect if data is coming
        var peekTask = Task.Run(() => stdin.Read(buf, 0, 1));
        var timeoutMs = options.ExplicitStdin ? Timeout.Infinite : options.StdinTimeout;

        if (await Task.WhenAny(peekTask, Task.Delay(timeoutMs)).ConfigureAwait(true) == peekTask
            && await peekTask.ConfigureAwait(true) > 0)
        {
            // First byte received — read the rest without timeout
            using var ms = new MemoryStream();
            await ms.WriteAsync(buf).ConfigureAwait(true);
            await stdin.CopyToAsync(ms).ConfigureAwait(true);
            ms.Position = 0;
            using var reader = new StreamReader(ms);
            var xml = await reader.ReadToEndAsync().ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(xml))
            {
                contextDocument = env.LoadFromString(xml, "stdin:");
            }
        }
    }
    loadSw.Stop();

    // Create and configure the query engine
    var engine = new QueryEngine(
        nodeProvider: env.Store,
        documentResolver: env.Store);

    // Compile the query — pass base URI for module import resolution
    var compileSw = Stopwatch.StartNew();
    var queryBaseUri = options.QueryFile != null
        ? new Uri(Path.GetFullPath(options.QueryFile)).AbsoluteUri
        : new Uri(Path.GetFullPath(".") + "/").AbsoluteUri;
    var compilationResult = engine.Compile(query, new CompilationOptions { BaseUri = queryBaseUri });
    compileSw.Stop();

    if (!compilationResult.Success)
    {
        var errorMessages = string.Join("; ", compilationResult.Errors.Select(e => e.Message));
        throw new XQueryRuntimeException("XPST0003", $"Compilation failed: {errorMessages}");
    }

    if (options.Timing)
    {
        if (options.Sources.Count > 0 || contextDocument != null)
        {
            await Console.Error.WriteLineAsync(
                $"  load:    {loadSw.Elapsed.TotalMilliseconds,8:F1} ms  ({env.Documents.Count} document(s))")
                .ConfigureAwait(true);
        }
        await Console.Error.WriteLineAsync(
            $"  compile: {compileSw.Elapsed.TotalMilliseconds,8:F1} ms")
            .ConfigureAwait(true);
    }

    // Show execution plan if requested
    if (options.ShowPlan)
    {
        await Console.Error.WriteLineAsync("Execution Plan:").ConfigureAwait(true);
        DumpPlan(compilationResult.ExecutionPlan!.Root, Console.Error, indent: 2);
        await Console.Error.WriteLineAsync().ConfigureAwait(true);
    }

    if (options.DryRun)
    {
        if (options.Timing)
        {
            totalSw.Stop();
            await Console.Error.WriteLineAsync(
                $"  total:   {totalSw.Elapsed.TotalMilliseconds,8:F1} ms")
                .ConfigureAwait(true);
        }
        await Console.Error.WriteLineAsync("Query compiled successfully.").ConfigureAwait(true);
        return 0;
    }

    // Execute with context document if available
    var serializer = new ResultSerializer(env.Store, Console.Out, outputMethod);

    var execSw = Stopwatch.StartNew();
    using var context = engine.CreateContext(
        initialContextItem: contextDocument,
        staticBaseUri: compilationResult.BaseUri);

    var itemCount = 0;
    await foreach (var result in compilationResult.ExecutionPlan!.ExecuteAsync(context))
    {
        // Adaptive serialization: separate items with newlines (XQuery Serialization §12)
        if (itemCount > 0 && outputMethod == OutputMethod.Adaptive)
            Console.WriteLine();
        else if (itemCount > 0 && outputMethod == OutputMethod.Text)
            Console.Write(' ');
        serializer.Serialize(result);
        itemCount++;
    }
    execSw.Stop();

    if (itemCount > 0)
    {
        serializer.WriteNewline();
    }

    if (options.Timing)
    {
        await Console.Error.WriteLineAsync(
            $"  execute: {execSw.Elapsed.TotalMilliseconds,8:F1} ms")
            .ConfigureAwait(true);
        totalSw.Stop();
        await Console.Error.WriteLineAsync(
            $"  total:   {totalSw.Elapsed.TotalMilliseconds,8:F1} ms")
            .ConfigureAwait(true);
    }

    return 0;
}
catch (Exception ex) when (ex is PhoenixmlDb.XQuery.Parser.XQueryParseException)
{
    await Console.Error.WriteLineAsync($"Parse error: {ex.Message}").ConfigureAwait(true);
    return 2;
}
catch (XQueryRuntimeException ex)
{
    await Console.Error.WriteLineAsync($"Runtime error [{ex.ErrorCode}]: {ex.Message}").ConfigureAwait(true);
    return 3;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(true);
    if (options.Verbose)
    {
        await Console.Error.WriteLineAsync(ex.StackTrace).ConfigureAwait(true);
    }

    throw;
}

static void DumpPlan(PhysicalOperator op, TextWriter writer, int indent)
{
    var prefix = new string(' ', indent);
    var typeName = op.GetType().Name;

    // Remove "Operator" suffix for readability
    if (typeName.EndsWith("Operator", StringComparison.Ordinal))
        typeName = typeName[..^8];
    if (typeName.EndsWith("Node", StringComparison.Ordinal))
        typeName = typeName[..^4];

    // Collect key properties (non-operator, non-method descriptors)
    var details = new List<string>();
    foreach (var prop in op.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (prop.Name is "EstimatedCost" or "EstimatedCardinality")
            continue;
        if (typeof(PhysicalOperator).IsAssignableFrom(prop.PropertyType))
            continue;
        if (typeof(IEnumerable<PhysicalOperator>).IsAssignableFrom(prop.PropertyType))
            continue;

        try
        {
            var val = prop.GetValue(op);
            if (val == null) continue;

            // Skip large/noisy values
            if (val is System.Delegate) continue;
            if (val is PhysicalOperator) continue;

            var str = val switch
            {
                string s => $"\"{s}\"",
                QName q => q.ToString(),
                bool b => b.ToString().ToLowerInvariant(),
                _ => val.ToString()
            };

            if (!string.IsNullOrEmpty(str) && str != val.GetType().FullName)
                details.Add($"{prop.Name}={str}");
        }
        catch (TargetInvocationException)
        {
            // Skip properties that throw during reflection
        }
    }

    var detailStr = details.Count > 0 ? $"  ({string.Join(", ", details)})" : "";
    writer.WriteLine($"{prefix}{typeName}{detailStr}");

    // Recurse into child operators
    foreach (var prop in op.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (typeof(PhysicalOperator).IsAssignableFrom(prop.PropertyType))
        {
            if (prop.GetValue(op) is PhysicalOperator child)
            {
                writer.WriteLine($"{prefix}  {prop.Name}:");
                DumpPlan(child, writer, indent + 4);
            }
        }
        else if (typeof(IEnumerable<PhysicalOperator>).IsAssignableFrom(prop.PropertyType))
        {
            if (prop.GetValue(op) is IEnumerable<PhysicalOperator> children)
            {
                var list = children.ToList();
                if (list.Count > 0)
                {
                    writer.WriteLine($"{prefix}  {prop.Name}:");
                    foreach (var child in list)
                        DumpPlan(child, writer, indent + 4);
                }
            }
        }
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage: xquery [options] <expression> [sources...]
               xquery [options] -f <query-file> [sources...]
               command | xquery [options] <expression>

        Execute XQuery expressions against XML files, directories, or URLs.

        Arguments:
          <expression>       Inline XQuery expression to execute
          [sources...]       XML files, directories, or URLs to query

        Options:
          -f, --file <path>  Read XQuery from a file instead of inline
          -o, --output <method>
                             Output method: adaptive (default), xml, text, json
          --stdin            Read XML input from stdin (waits indefinitely)
          --timeout <ms>     Stdin auto-detection timeout in ms (default: 200)
          --timing           Show parse/compile/execute timing breakdown
          --plan             Show the execution plan before running
          --dry-run          Parse and compile only, do not execute
          -v, --verbose      Show detailed error information
          -h, --help         Show this help message
          --version          Show version information

        Sources:
          Files              Path to an XML file (loaded as fn:doc() target)
          Directories        Path to a directory (all *.xml files loaded)
          URLs               HTTP/HTTPS URL to fetch XML from

        When a single source is provided, it becomes the context item (.).
        All sources are available via fn:doc($uri) by their path or URL.
        fn:collection() returns all loaded documents.

        Examples:
          xquery '//title' books.xml
          xquery 'count(//item)' catalog.xml
          xquery -f transform.xq input.xml
          xquery 'collection()//product' ./data/
          xquery --plan 'for $x in 1 to 10 return $x * $x'
          xquery --timing '//item' large-catalog.xml
          xquery --dry-run -f complex-query.xq
          curl http://example.com/data.xml | xquery '//item/@name'
        """);
}

/// <summary>
/// Parsed command-line options.
/// </summary>
file sealed class CliOptions
{
    public string? Query { get; init; }
    public string? QueryFile { get; init; }
    public List<string> Sources { get; init; } = [];
    public OutputMethod OutputMethod { get; init; } = OutputMethod.Adaptive;
    public bool OutputMethodExplicit { get; init; }
    public bool ReadStdin { get; init; }
    public bool ExplicitStdin { get; init; }
    public int StdinTimeout { get; init; } = 200;
    public bool Timing { get; init; }
    public bool ShowPlan { get; init; }
    public bool DryRun { get; init; }
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }
    public bool Verbose { get; init; }

    public static CliOptions Parse(string[] args)
    {
        string? query = null;
        string? queryFile = null;
        var sources = new List<string>();
        var outputMethod = OutputMethod.Adaptive;
        var outputMethodExplicit = false;
        var readStdin = false;
        var explicitStdin = false;
        var stdinTimeout = 200;
        var timing = false;
        var showPlan = false;
        var dryRun = false;
        var showHelp = false;
        var showVersion = false;
        var verbose = false;
        var expectingFile = false;
        var expectingOutput = false;
        var expectingTimeout = false;

        foreach (var arg in args)
        {
            if (expectingFile)
            {
                queryFile = arg;
                expectingFile = false;
                continue;
            }

            if (expectingOutput)
            {
                outputMethod = arg.ToLowerInvariant() switch
                {
                    "xml" => OutputMethod.Xml,
                    "text" => OutputMethod.Text,
                    "json" => OutputMethod.Json,
                    _ => OutputMethod.Adaptive
                };
                outputMethodExplicit = true;
                expectingOutput = false;
                continue;
            }

            if (expectingTimeout)
            {
                if (int.TryParse(arg, out var ms))
                    stdinTimeout = ms;
                expectingTimeout = false;
                continue;
            }

            switch (arg)
            {
                case "-h" or "--help":
                    showHelp = true;
                    break;
                case "--version":
                    showVersion = true;
                    break;
                case "-f" or "--file":
                    expectingFile = true;
                    break;
                case "-o" or "--output":
                    expectingOutput = true;
                    break;
                case "--stdin":
                    readStdin = true;
                    explicitStdin = true;
                    break;
                case "--timeout":
                    expectingTimeout = true;
                    break;
                case "--timing":
                    timing = true;
                    break;
                case "--plan":
                    showPlan = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    if (query == null && queryFile == null)
                        query = arg;
                    else
                        sources.Add(arg);
                    break;
            }
        }

        // Auto-detect stdin when piped and no sources given
        if (!readStdin && sources.Count == 0 && !Console.IsInputRedirected)
        {
            // Not piped, no stdin
        }
        else if (!readStdin && sources.Count == 0 && Console.IsInputRedirected)
        {
            readStdin = true;
        }

        return new CliOptions
        {
            Query = query,
            QueryFile = queryFile,
            Sources = sources,
            OutputMethod = outputMethod,
            OutputMethodExplicit = outputMethodExplicit,
            ReadStdin = readStdin,
            ExplicitStdin = explicitStdin,
            StdinTimeout = stdinTimeout,
            Timing = timing,
            ShowPlan = showPlan,
            DryRun = dryRun,
            ShowHelp = showHelp,
            ShowVersion = showVersion,
            Verbose = verbose
        };
    }
}
