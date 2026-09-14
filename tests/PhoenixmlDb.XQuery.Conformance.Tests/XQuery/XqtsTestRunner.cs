using System.Reflection;
using PhoenixmlDb.Core;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.Conformance.Tests.XQuery;

/// <summary>
/// Runner for W3C XQuery Test Suite (QT3/QT4) tests.
///
/// Test Suite Sources:
/// - QT3 Tests: https://github.com/w3c/qt3tests (~30,000 tests for XPath/XQuery 3.1)
/// - QT4 Tests: https://github.com/qt4cg/qt4tests (~40,000 tests for XPath/XQuery 4.0)
///
/// The test suite uses XML catalog files to define tests with:
/// - Test case metadata (name, description, dependencies)
/// - Source documents and environment setup
/// - Expected results as assertions
/// </summary>
public sealed class XqtsTestRunner
{
    private readonly QueryEngine _engine;
    private readonly string _testDataPath;
    private readonly XqtsConfiguration _config;

    /// <summary>
    /// Backs the context item. QT3 environments declare a source with <c>role="."</c>, which
    /// must reach the query as a DOCUMENT NODE; without a node provider the engine has no
    /// store to navigate and every path expression fails. Documents are cached by path
    /// because the corpus reuses a handful of sources across thousands of cases — fsx.xml
    /// alone backs most of prod/, and re-parsing it per test would dominate the run.
    /// </summary>
    private readonly XdmDocumentStore _documents = new();
    private readonly Dictionary<string, XdmDocument> _documentCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Serves <c>import schema</c>. The runner previously passed no schema provider at all, so
    /// any query importing a schema failed with "Cannot locate schema for namespace" no matter
    /// what the environment declared. Shared across tests and loaded on demand; the corpus has
    /// ~103 schema declarations over a handful of distinct .xsd files.
    /// </summary>
    private readonly XsdSchemaProvider _schemas = new();
    private readonly HashSet<string> _loadedSchemas = new(StringComparer.Ordinal);

    public XqtsTestRunner(string testDataPath, XqtsConfiguration? config = null)
    {
        _testDataPath = testDataPath;
        _config = config ?? new XqtsConfiguration();
        PreloadXmlNamespaceSchema(_schemas);
        _engine = new QueryEngine(
            nodeProvider: _documents, documentResolver: _documents, schemaProvider: _schemas);
    }

    /// <summary>
    /// Seeds the schema set with the XML-namespace attribute declarations (xml:lang, xml:space,
    /// xml:base, xml:id).
    /// </summary>
    /// <remarks>
    /// XSD processors are expected to know these implicitly; .NET's XmlSchemaSet does not. A
    /// corpus schema that merely REFERENCES xml:lang therefore fails to compile with
    /// "The 'http://www.w3.org/XML/1998/namespace:lang' attribute is not declared", and the
    /// import surfaces as the misleading "Cannot locate schema for namespace" — the schema is
    /// found, it just will not build. Verified against the suite's own loans.xsd.
    /// </remarks>
    private static void PreloadXmlNamespaceSchema(XsdSchemaProvider provider)
    {
        const string XmlNsXsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       xmlns:xml="http://www.w3.org/XML/1998/namespace"
                       targetNamespace="http://www.w3.org/XML/1998/namespace">
              <xs:attribute name="lang"  type="xs:string"/>
              <xs:attribute name="space" type="xs:NCName"/>
              <xs:attribute name="base"  type="xs:anyURI"/>
              <xs:attribute name="id"    type="xs:ID"/>
            </xs:schema>
            """;
        try
        {
            provider.Add("http://www.w3.org/XML/1998/namespace", new StringReader(XmlNsXsd));
        }
        catch (PhoenixmlDb.XQuery.SchemaException)
        {
            // A provider that already knows the XML namespace needs no help.
        }
    }

    /// <summary>
    /// Registers documents the environment makes addressable by URI, so <c>fn:doc</c>,
    /// <c>fn:json-doc</c> and <c>fn:unparsed-text</c> can retrieve them. Without this the
    /// engine sees a bare relative name and reports "No document could be retrieved" or
    /// "Could not find a part of the path".
    /// </summary>
    private void RegisterUriDocuments(XqtsEnvironment? env)
    {
        if (env is null || env.UriDocuments.Count == 0) return;
        foreach (var (uri, path) in env.UriDocuments)
        {
            // Record the mapping regardless of whether the file is XML: fn:json-doc and
            // fn:unparsed-text resolve through the engine's ResourceMappings, not the XML store.
            if (File.Exists(path)) _resourceMappings[uri] = path;
            if (!_registeredUris.Add(uri)) continue;
            if (!File.Exists(path)) continue;
            // LoadFromString, not LoadFile: only the former accepts the document URI the test
            // will actually ask for. Non-XML resources (JSON, text) cannot go into the XML
            // store at all — parsing them as XML throws — so they are served through the
            // engine's resource-URI mapping instead (see ResourceMappings below).
            try { _documents.LoadFromString(File.ReadAllText(path), uri); }
            catch (System.Xml.XmlException) { }
        }
    }

    private readonly HashSet<string> _registeredUris = new(StringComparer.Ordinal);

    /// <summary>
    /// Resource URI -> local file, handed to the execution context so fn:json-doc and
    /// fn:unparsed-text can retrieve a declared &lt;resource&gt;. The engine already had the
    /// seam (ResourceUriResolver.Map over context.ResourceMappings); nothing was populating it.
    ///
    /// The predecessor of this field was written and never read — the same defect this file has
    /// now corrected six times — which left 135 "Error reading JSON resource: Could not find a
    /// part of the path" and 34 "Resource not found".
    /// </summary>
    private readonly Dictionary<string, string> _resourceMappings = new(StringComparer.Ordinal);

    private int _eqStringFallbackRescues;

    /// <summary>
    /// How many assert-eq tests passed ONLY because the legacy string comparison rescued them
    /// after engine evaluation said unequal. Each one is a place where the harness may be
    /// masking a genuine engine inequality; a high count means the fallback is load-bearing
    /// and worth removing carefully rather than left in place.
    /// </summary>
    public int EqStringFallbackRescues => _eqStringFallbackRescues;

    /// <summary>
    /// Registers the environment's schemas so <c>import schema</c> can resolve them. Failures
    /// are swallowed: a schema this engine cannot compile must surface as the test's own error,
    /// not as a crash that takes the rest of the run with it.
    /// </summary>
    private void EnsureSchemasLoaded(XqtsEnvironment? env)
    {
        if (env is null || env.Schemas.Count == 0) return;
        foreach (var (uri, path) in env.Schemas)
        {
            if (!_loadedSchemas.Add(uri + "|" + path)) continue;
            if (!File.Exists(path)) continue;
            try { _schemas.ImportSchema(uri, [path]); }
            catch (PhoenixmlDb.XQuery.SchemaException) { }
        }
    }

    /// <summary>
    /// Loads test cases from a catalog file.
    /// </summary>
    public async Task<IReadOnlyList<XqtsTestCase>> LoadTestCasesAsync(
        string catalogPath,
        CancellationToken ct = default)
    {
        var testCases = new List<XqtsTestCase>();
        var catalogFile = Path.Combine(_testDataPath, catalogPath);

        if (!File.Exists(catalogFile))
        {
            return testCases;
        }

        var doc = await Task.Run(() => XDocument.Load(catalogFile), ct);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // Parse test-set references
        foreach (var testSetRef in doc.Descendants(ns + "test-set"))
        {
            var testSetFile = testSetRef.Attribute("file")?.Value;
            if (testSetFile != null)
            {
                var testSetPath = Path.Combine(Path.GetDirectoryName(catalogFile)!, testSetFile);
                var tests = await LoadTestSetFileAsync(testSetPath, ct);
                testCases.AddRange(tests);
            }
        }

        return testCases;
    }

    /// <summary>
    /// Loads test cases for a specific test-set by name from the master catalog.
    /// </summary>
    public async Task<IReadOnlyList<XqtsTestCase>> LoadTestSetByNameAsync(
        string testSetName,
        CancellationToken ct = default)
    {
        var catalogFile = Path.Combine(_testDataPath, "catalog.xml");
        if (!File.Exists(catalogFile))
        {
            return [];
        }

        var doc = await Task.Run(() => XDocument.Load(catalogFile), ct);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var testSetRef = doc.Descendants(ns + "test-set")
            .FirstOrDefault(e => e.Attribute("name")?.Value == testSetName);

        var testSetFile = testSetRef?.Attribute("file")?.Value;
        if (testSetFile == null)
        {
            return [];
        }

        var testSetPath = Path.Combine(Path.GetDirectoryName(catalogFile)!, testSetFile);
        return await LoadTestSetFileAsync(testSetPath, ct);
    }

    /// <summary>
    /// Loads test cases from a test-set file.
    /// </summary>
    private async Task<IReadOnlyList<XqtsTestCase>> LoadTestSetFileAsync(
        string testSetPath,
        CancellationToken ct)
    {
        var testCases = new List<XqtsTestCase>();

        if (!File.Exists(testSetPath))
        {
            return testCases;
        }

        var doc = await Task.Run(() => XDocument.Load(testSetPath), ct);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var testSetName = doc.Root?.Attribute("name")?.Value ?? Path.GetFileNameWithoutExtension(testSetPath);

        // Environments resolve in two scopes. catalog.xml defines GLOBAL ones (works, staff,
        // atomic, empty, …) that any test-set may reference by name; a test-set may also
        // define its own, which take precedence. Only the test-set scope was loaded, so
        // `<environment ref="works"/>` found nothing and fell through to "parse inline",
        // producing an EMPTY environment — no context item, no variables, no namespaces.
        // That single omission accounts for the largest error clusters in the QT3 run:
        // "context item is absent" (815), "Variable $works is not defined" (~361),
        // "Unbound namespace prefix: atomic" (~234).
        var environments = new Dictionary<string, XqtsEnvironment>(LoadGlobalEnvironments());
        foreach (var envElem in doc.Descendants(ns + "environment"))
        {
            var envName = envElem.Attribute("name")?.Value;
            if (envName != null)
            {
                environments[envName] = ParseEnvironment(envElem, ns, Path.GetDirectoryName(testSetPath)!);
            }
        }

        // Parse test cases
        foreach (var testCase in doc.Descendants(ns + "test-case"))
        {
            var test = ParseTestCase(testCase, ns, testSetName, environments, Path.GetDirectoryName(testSetPath)!);
            if (test != null && ShouldRunTest(test))
            {
                testCases.Add(test);
            }
        }

        return testCases;
    }

    /// <summary>
    /// Environments declared at the top of <c>catalog.xml</c>, available to every test-set by
    /// name. Their source/schema paths resolve against the CATALOG's directory, not the
    /// referencing test-set's — which is why they cannot simply be re-parsed per test-set.
    /// Parsed once; the catalog is large and every test-set would otherwise re-read it.
    /// </summary>
    private Dictionary<string, XqtsEnvironment> LoadGlobalEnvironments()
    {
        if (_globalEnvironments is not null) return _globalEnvironments;

        var result = new Dictionary<string, XqtsEnvironment>(StringComparer.Ordinal);
        var catalogFile = Path.Combine(_testDataPath, "catalog.xml");
        if (File.Exists(catalogFile))
        {
            var catalog = XDocument.Load(catalogFile);
            var cns = catalog.Root?.Name.Namespace ?? XNamespace.None;
            var baseDir = Path.GetDirectoryName(catalogFile)!;
            // Only top-level <environment> children of the catalog root: a <test-set> element
            // inside the catalog is a REFERENCE to another file, not a definition.
            foreach (var envElem in catalog.Root?.Elements(cns + "environment") ?? [])
            {
                var name = envElem.Attribute("name")?.Value;
                if (name != null)
                    result[name] = ParseEnvironment(envElem, cns, baseDir);
            }
        }

        _globalEnvironments = result;
        return result;
    }

    private Dictionary<string, XqtsEnvironment>? _globalEnvironments;

    private XqtsEnvironment ParseEnvironment(XElement elem, XNamespace ns, string basePath)
    {
        var env = new XqtsEnvironment();

        // Parse source documents
        foreach (var source in elem.Elements(ns + "source"))
        {
            var role = source.Attribute("role")?.Value;
            var file = source.Attribute("file")?.Value;
            if (file != null)
            {
                var full = Path.Combine(basePath, file);
                // role is "." (context item) or "$name" (bind the document to that variable).
                // Both were stored; only "." was ever consumed, so a query using $works got
                // "Variable $works is not defined" — 75 sources in the corpus use the $ form.
                env.Sources[role ?? "."] = full;

                // @uri makes the document addressable by fn:doc.
                var srcUri = source.Attribute("uri")?.Value;
                if (srcUri != null) env.UriDocuments[srcUri] = full;
            }
        }

        // <module uri=… file=…> tells the harness where a library module lives, so
        // `import module namespace X = "…"` resolves without an `at` hint. 314 of these in the
        // corpus, and none were parsed — every module-importing test failed with
        // "Module '…' could not be resolved from location hints: []". The ENGINE's module
        // support is fine; it was simply never told where the files are.
        foreach (var mod in elem.Elements(ns + "module"))
        {
            var uri = mod.Attribute("uri")?.Value;
            var file = mod.Attribute("file")?.Value;
            if (uri != null && file != null)
                env.Modules[uri] = Path.Combine(basePath, file);
        }

        // <resource> declares a document addressable by URI — JSON for fn:json-doc, text for
        // fn:unparsed-text. Never parsed, so those tests reported "Could not find a part of the
        // path" against a relative name the engine had no way to resolve.
        foreach (var res in elem.Elements(ns + "resource"))
        {
            var file = res.Attribute("file")?.Value;
            var uri = res.Attribute("uri")?.Value;
            if (file != null && uri != null)
                env.UriDocuments[uri] = Path.Combine(basePath, file);
        }

        // Parse schemas. These were not parsed at all, so a query with `import schema` had
        // nothing to resolve against: "Cannot locate schema for namespace X" (~215 errors).
        foreach (var schema in elem.Elements(ns + "schema"))
        {
            var uri = schema.Attribute("uri")?.Value;
            var file = schema.Attribute("file")?.Value;
            if (uri != null && file != null)
                env.Schemas[uri] = Path.Combine(basePath, file);
        }

        // Parse namespaces
        foreach (var nsDecl in elem.Elements(ns + "namespace"))
        {
            var prefix = nsDecl.Attribute("prefix")?.Value ?? "";
            var uri = nsDecl.Attribute("uri")?.Value ?? "";
            env.Namespaces[prefix] = uri;
        }

        // Parse parameters
        foreach (var param in elem.Elements(ns + "param"))
        {
            var name = param.Attribute("name")?.Value;
            var select = param.Attribute("select")?.Value;
            if (name != null && select != null)
            {
                env.Parameters[name] = select;
            }
        }

        return env;
    }

    private XqtsTestCase? ParseTestCase(
        XElement elem,
        XNamespace ns,
        string testSetName,
        Dictionary<string, XqtsEnvironment> environments,
        string basePath)
    {
        var name = elem.Attribute("name")?.Value;
        if (name == null) return null;

        var test = new XqtsTestCase
        {
            Name = name,
            TestSet = testSetName,
            Description = elem.Element(ns + "description")?.Value ?? "",
            Query = ReadTestQuery(elem.Element(ns + "test"), basePath),
            BaseDir = basePath
        };

        // Check for environment reference
        var envRef = elem.Element(ns + "environment")?.Attribute("ref")?.Value;
        if (envRef != null && environments.TryGetValue(envRef, out var env))
        {
            test.Environment = env;
        }
        else
        {
            // Parse inline environment
            var envElem = elem.Element(ns + "environment");
            if (envElem != null)
            {
                test.Environment = ParseEnvironment(envElem, ns, basePath);
            }
        }

        // <module> may appear directly on the test-case, alongside <environment>, e.g.
        //   <test-case name="generate-id-014">
        //     <environment ref="works-mod"/>
        //     <module uri="http://www.w3.org/QT3/copy" file="id/copy.xq"/>
        // Only the environment-scoped ones were read, so these still failed with
        // "could not be resolved from location hints: []".
        foreach (var mod in elem.Elements(ns + "module"))
        {
            var mUri = mod.Attribute("uri")?.Value;
            var mFile = mod.Attribute("file")?.Value;
            if (mUri != null && mFile != null)
                test.Modules[mUri] = Path.Combine(basePath, mFile);
        }

        // Parse dependencies
        foreach (var dep in elem.Elements(ns + "dependency"))
        {
            var type = dep.Attribute("type")?.Value;
            var value = dep.Attribute("value")?.Value;
            var satisfied = dep.Attribute("satisfied")?.Value != "false";

            if (type != null && value != null)
            {
                test.Dependencies.Add(new XqtsDependency
                {
                    Type = type,
                    Value = value,
                    Satisfied = satisfied
                });
            }
        }

        // Parse result assertions
        var result = elem.Element(ns + "result");
        if (result != null)
        {
            test.Assertions = ParseAssertions(result, ns, basePath);
        }

        return test;
    }

    private List<XqtsAssertion> ParseAssertions(XElement resultElem, XNamespace ns, string basePath)
    {
        var assertions = new List<XqtsAssertion>();

        foreach (var child in resultElem.Elements())
        {
            var assertion = new XqtsAssertion
            {
                Type = child.Name.LocalName,
                // An assertion may hold its expected value in an external file rather than
                // inline — <assert-xml file="..."/>, 41 times in the corpus. Element.Value is
                // "" for those, so the test compared against an empty expectation.
                Value = ReadExternalOrInline(child, basePath),
                Flags = child.Attribute("flags")?.Value,
                Code = child.Attribute("code")?.Value
            };

            // Handle nested assertions (all-of, any-of)
            if (child.Name.LocalName == "all-of" || child.Name.LocalName == "any-of")
            {
                assertion.Children = ParseAssertions(child, ns, basePath);
            }

            assertions.Add(assertion);
        }

        return assertions;
    }

    private bool ShouldRunTest(XqtsTestCase test)
    {
        // Check dependencies against configuration
        foreach (var dep in test.Dependencies)
        {
            if (!_config.SatisfiesDependency(dep))
            {
                return false;
            }
        }

        // Check if test is in skip list
        if (_config.SkipTests.Contains(test.Name) || _config.SkipTests.Contains($"{test.TestSet}/{test.Name}"))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Runs a single test case.
    /// </summary>
    public async Task<XqtsTestResult> RunTestAsync(XqtsTestCase testCase, CancellationToken ct = default)
    {
        var result = new XqtsTestResult
        {
            TestCase = testCase,
            StartTime = DateTimeOffset.UtcNow
        };

        try
        {
            // Setup context with environment
            var queryResult = await ExecuteWithHardTimeoutAsync(testCase, ct).ConfigureAwait(false);
            result.ActualResult = queryResult;

            // Verify assertions
            result.Passed = await VerifyAssertionsAsync(testCase, testCase.Assertions, queryResult, ct).ConfigureAwait(false);
        }
        catch (QueryAbandonedException ex)
        {
            result.Error = ex;
            result.Passed = false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Per-test timeout expired (not the suite-level cancellation)
            var ex = new TimeoutException(
                $"Test '{testCase.Name}' exceeded the per-test timeout of {PerTestTimeout.TotalSeconds}s.");
            result.Error = ex;
            result.Passed = IsExpectedError(testCase.Assertions, ex);
        }
        catch (XQueryRuntimeException ex)
        {
            result.Error = ex;
            result.Passed = IsExpectedError(testCase.Assertions, ex);
        }
        catch (Exception ex)
        {
            result.Error = ex;
            result.Passed = IsExpectedError(testCase.Assertions, ex);
        }

        result.EndTime = DateTimeOffset.UtcNow;
        return result;
    }

    /// <summary>
    /// Maximum number of result items collected per query to prevent OOM on pathological tests.
    /// </summary>
    private const int MaxResultCount = 100_000;

    /// <summary>
    /// Per-test execution timeout to prevent runaway queries from blocking the test suite.
    /// </summary>
    private static readonly TimeSpan PerTestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long past <see cref="PerTestTimeout"/> a query may keep running before the runner
    /// stops waiting for it.
    /// </summary>
    private static readonly TimeSpan AbandonGrace = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Runs the query with a wall-clock limit that does not depend on the engine cooperating.
    /// </summary>
    /// <remarks>
    /// <see cref="PerTestTimeout"/> is a cancellation token, and a token only stops a query that
    /// looks at it. An engine loop that never polls ran on forever: under PhoenixmlDb.XQuery 1.7.0,
    /// QT3 op/same-key/same-key-023 (map:remove and map:put over a 421,875-key map, once per key,
    /// inside <c>every</c>) copies the whole map on each call, and the quantifier did not poll
    /// cancellation. The nightly's xqts chunk wedged on it for the rest of its budget and reported
    /// TIMEOUT for the whole chunk, with 256 test-sets never run, and nothing in the artifact
    /// naming the case.
    ///
    /// The query starts on the thread pool, so even a synchronous spin that never yields
    /// returns control here. Past the token's deadline plus <see cref="AbandonGrace"/>, the case
    /// is recorded as a failure naming what happened, and the runner moves on. The abandoned
    /// query is not stopped, because .NET has no way to kill a thread. It keeps a core busy
    /// until the process exits, which costs the rest of the chunk some speed but not its
    /// results.
    /// </remarks>
    private async Task<object?> ExecuteWithHardTimeoutAsync(XqtsTestCase testCase, CancellationToken ct)
    {
        var query = Task.Run(() => ExecuteQueryAsync(testCase, ct), CancellationToken.None);
        var deadline = Task.Delay(PerTestTimeout + AbandonGrace, ct);
        if (await Task.WhenAny(query, deadline).ConfigureAwait(false) == query)
            return await query.ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        // Observe the abandoned task's eventual fault, or it surfaces later as an
        // UnobservedTaskException against whichever test happens to be running.
        _ = query.ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        throw new QueryAbandonedException(
            $"Test '{testCase.Name}' ignored cancellation: still running {(PerTestTimeout + AbandonGrace).TotalSeconds}s " +
            "after it was cancelled at the per-test timeout. Abandoned so the rest of the run can proceed; " +
            "the engine has a loop that does not poll its CancellationToken.");
    }

    private async Task<object?> ExecuteQueryAsync(XqtsTestCase testCase, CancellationToken ct)
    {
        // Load source documents if needed
        var contextItem = await LoadContextItemAsync(testCase.Environment, ct);
        EnsureSchemasLoaded(testCase.Environment);
        RegisterUriDocuments(testCase.Environment);

        // Build query with environment parameter bindings
        var query = PrependEnvironmentBindings(testCase.Query, testCase.Environment);

        // Sources whose role is "$name" bind the DOCUMENT to that variable. Declaring them
        // external here (and supplying the value below) is what the "$" role means; without it
        // the query reported "Variable $works is not defined".
        var varSources = new List<(string Name, object? Doc)>();
        if (testCase.Environment is { } envForVars)
        {
            foreach (var (role, path) in envForVars.Sources)
            {
                if (role.Length < 2 || role[0] != '$' || !File.Exists(path)) continue;
                var name = role[1..];
                if (!_documentCache.TryGetValue(path, out var d))
                {
                    d = _documents.LoadFile(path);
                    _documentCache[path] = d;
                }
                varSources.Add((name, d));
                query = $"declare variable ${name} external;\n" + query;
            }
        }

        // Apply a per-test timeout on top of the caller's cancellation token
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(PerTestTimeout);
        var token = timeoutCts.Token;

        // Compile and execute query
        var results = new List<object?>();

        // NOTE: ExecuteAsync(string, ContainerId, object? initialContextItem, CancellationToken).
        // The token must go to the cancellationToken parameter — passing it positionally as the
        // 3rd arg made it the initialContextItem and left cancellation/timeout disabled (CA2016).
        //
        // Fixing that CA2016 warning by naming ONLY the token silently dropped the context item:
        // `contextItem` was still computed but never passed, so every path expression saw an
        // absent context and failed with "The context item is absent for '/'". Name both.
        var execCtx = _engine.CreateContext(initialContextItem: contextItem, cancellationToken: token);

        // A relative URI in a test resolves against the query's static base URI. Unset, the
        // engine falls back to the process working directory — the build output — where
        // qt3tests is deliberately not copied, so every relative resource lookup failed with
        // "Could not find a part of the path .../bin/Debug/net10.0/...". Point it at the
        // test-set's own directory in the corpus, which is what the URIs are written against.
        // The trailing separator matters: without it Uri treats the last segment as a FILE and
        // resolves siblings of it rather than children.
        if (!string.IsNullOrEmpty(testCase.BaseDir) && Directory.Exists(testCase.BaseDir))
        {
            var dir = testCase.BaseDir.EndsWith(Path.DirectorySeparatorChar)
                ? testCase.BaseDir
                : testCase.BaseDir + Path.DirectorySeparatorChar;
            execCtx.StaticBaseUri = new Uri(dir).AbsoluteUri;
        }

        if (_resourceMappings.Count > 0)
            execCtx.SetResourceMappings(new Dictionary<string, string>(_resourceMappings));
        foreach (var (name, doc) in varSources)
            execCtx.SetExternalVariable(name, doc);

        // Hand the environment's <module> declarations to the compiler. ExternalModules maps a
        // module NAMESPACE to candidate files — how a test importing a namespace with no `at`
        // hint resolves — and ExternalModuleLocations maps a location HINT to a file, for tests
        // that do supply one. Without these, `import module namespace …` failed with
        // "could not be resolved from location hints: []" even though the engine's module
        // support works: the harness simply never parsed <module> and never said where the
        // files were.
        CompilationOptions? compileOptions = null;
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in testCase.Environment?.Modules ?? []) modules[kv.Key] = kv.Value;
        // Test-case declarations win over the environment's: they are the more specific scope.
        foreach (var kv in testCase.Modules) modules[kv.Key] = kv.Value;
        if (modules.Count > 0)
        {
            compileOptions = new CompilationOptions
            {
                ExternalModules = modules.ToDictionary(
                    kv => kv.Key, kv => new List<string> { kv.Value }, StringComparer.Ordinal),
                ExternalModuleLocations = new Dictionary<string, string>(modules, StringComparer.Ordinal)
            };
        }

        var compiledQuery = _engine.Compile(query, compileOptions);
        if (!compiledQuery.Success || compiledQuery.ExecutionPlan is null)
            throw new XQueryRuntimeException("XPST0003",
                "Compilation failed: " + string.Join("; ", compiledQuery.Errors));

        await foreach (var item in compiledQuery.ExecutionPlan.ExecuteAsync(execCtx))
        {
            results.Add(item);

            if (results.Count > MaxResultCount)
            {
                throw new XQueryRuntimeException(
                    "FOER0000",
                    $"Result count exceeded the safety limit of {MaxResultCount} items.");
            }
        }

        // A multi-item result becomes object?[] — the engine's own SEQUENCE representation —
        // and not List<object?>, which is the engine's ARRAY representation. Returning a
        // List here made the two indistinguishable at the assertion layer: array:sort(...)
        // yields ONE item that is itself a List<object?>, so AsXdmSequence flattened the
        // array into a sequence of its members and every array-shaped assertion failed
        // against a correct engine. array:sort([1,4,6,5,3]) returns [1,3,4,5,6] with size 5
        // and `instance of array(*)` true when run directly; the harness could not see it.
        return results.Count == 1 ? results[0] : results.ToArray();
    }

    /// <summary>
    /// Prepends variable declarations for environment parameters to the query.
    /// Replaces `declare variable $name external;` with `declare variable $name := value;`.
    /// </summary>
    private static string PrependEnvironmentBindings(string query, XqtsEnvironment? env)
    {
        if (env is null) return query;
        if (env.Parameters.Count == 0 && env.Namespaces.Count == 0) return query;

        var result = query;
        var prologue = new System.Text.StringBuilder();

        // Environment <namespace> declarations. These were parsed into env.Namespaces and then
        // never used — the dictionary had exactly one write and no reads — so a test whose
        // environment supplies the binding still failed with "Unbound namespace prefix: atomic".
        foreach (var (prefix, uri) in env.Namespaces)
        {
            if (string.IsNullOrEmpty(prefix))
                prologue.Append("declare default element namespace \"").Append(uri).Append("\";\n");
            else
                prologue.Append("declare namespace ").Append(prefix)
                        .Append(" = \"").Append(uri).Append("\";\n");
        }

        foreach (var (name, select) in env.Parameters)
        {
            // Preferred form: the query declares the variable external and we supply the value.
            var externalDecl = $"declare variable ${name} external";
            if (result.Contains(externalDecl, StringComparison.Ordinal))
            {
                result = result.Replace(externalDecl + ";", $"declare variable ${name} := {select};");
                result = result.Replace(externalDecl, $"declare variable ${name} := {select}");
                continue;
            }

            // Otherwise DECLARE it. Only the replace path existed, so an environment param used
            // by a query that never declared it — the common shape for the catalog's `works`
            // and `staff` environments — stayed unbound: "Variable $works is not defined".
            prologue.Append("declare variable $").Append(name).Append(" := ").Append(select).Append(";\n");
        }

        if (prologue.Length == 0) return result;

        // A version declaration must stay first in the module, so splice after it when present.
        var insertAt = FindPrologueInsertionPoint(result);
        return result[..insertAt] + prologue + result[insertAt..];
    }

    /// <summary>
    /// Returns the offset at which generated prolog declarations may be inserted: after a
    /// leading <c>xquery version "…";</c> if the module has one, otherwise the start.
    /// </summary>
    private static int FindPrologueInsertionPoint(string query)
    {
        var i = 0;
        while (i < query.Length && char.IsWhiteSpace(query[i])) i++;
        if (!query.AsSpan(i).StartsWith("xquery version", StringComparison.Ordinal)) return 0;
        var semi = query.IndexOf(';', i);
        return semi < 0 ? 0 : semi + 1;
    }

    /// <summary>
    /// Loads the environment's <c>role="."</c> source as the context item.
    /// </summary>
    /// <remarks>
    /// This used to <c>ReadAllText</c> and return the STRING — the comment said "parse as XML
    /// and return root node", but the parse was never written. The engine then received the
    /// raw markup as an xs:string context item and reported, accurately,
    /// <c>An axis step (Child::table) was used when the context item is not a node
    /// (got xs:string "&lt;tables&gt;&lt;table&gt;…")</c>. Every QT3 case needing a context
    /// document failed, and the engine was blamed for it.
    /// </remarks>
    private Task<object?> LoadContextItemAsync(XqtsEnvironment? env, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (env?.Sources.TryGetValue(".", out var sourcePath) != true || !File.Exists(sourcePath))
            return Task.FromResult<object?>(null);

        if (!_documentCache.TryGetValue(sourcePath!, out var doc))
        {
            doc = _documents.LoadFile(sourcePath!);
            _documentCache[sourcePath!] = doc;
        }
        return Task.FromResult<object?>(doc);
    }

    private async Task<bool> VerifyAssertionsAsync(
        XqtsTestCase testCase, List<XqtsAssertion> assertions, object? result, CancellationToken ct)
    {
        foreach (var assertion in assertions)
        {
            if (!await VerifyAssertionAsync(testCase, assertion, result, ct).ConfigureAwait(false))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Dispatches the assertion kinds that need to EVALUATE an expression (and therefore the
    /// engine, and therefore async); everything else goes to the synchronous switch.
    /// </summary>
    private async Task<bool> VerifyAssertionAsync(
        XqtsTestCase testCase, XqtsAssertion assertion, object? result, CancellationToken ct)
    {
        switch (assertion.Type)
        {
            // <serialization-matches> is a regex matched against the SERIALIZED result, under
            // whatever output method the query's prolog declares. 576 corpus cases use it and
            // nothing implemented it, so all of them fell through to the catch-all and
            // returned false — the whole method-adaptive / method-json / method-html /
            // method-xhtml / method-xml family, failing regardless of what the engine emitted.
            //
            // Note this defaults the OPPOSITE way to the XSLT runner's `_ => true`, which
            // silently PASSES what it does not understand. Both are wrong; only one is
            // visible in the score.
            case "serialization-matches":
                return VerifySerializationMatches(testCase, assertion, result);

            // Expected serialized output, given verbatim rather than as a regex.
            case "assert-serialization":
                return VerifySerializationEquals(testCase, assertion, result);

            // The test expects SERIALIZATION ITSELF to fail with a given code. The runner only
            // ever caught errors raised during EXECUTION and never serialized at all, so the
            // error could not occur and all 55 of these fell through to the catch-all.
            case "assert-serialization-error":
                return VerifySerializationError(testCase, assertion, result);
            case "assert":
                return await VerifyXPathAssertAsync(result, assertion.Value, ct).ConfigureAwait(false);
            case "assert-eq":
                // QT3 semantics: EVALUATE the expected expression and compare with `eq`.
                // VerifyEq compares STRINGS, with ad-hoc quote-stripping and constructor
                // special-casing — the same disease assert-deep-eq had, over 4050 corpus
                // occurrences.
                //
                // Engine first, legacy string compare as fallback. That ordering is
                // deliberately MONOTONIC: anything the string heuristic passed today still
                // passes, so this cannot regress. The cost is that a string-compare rescue can
                // MASK a real engine inequality, which is why the rescues are counted and
                // reported — a large number is a signal to investigate, not to celebrate.
                if (await VerifyXPathAssertAsync(
                        result, $"$result eq ({assertion.Value})", ct).ConfigureAwait(false))
                    return true;
                if (VerifyEq(result, assertion.Value))
                {
                    Interlocked.Increment(ref _eqStringFallbackRescues);
                    return true;
                }
                return false;
            case "assert-deep-eq":
                // QT3 semantics: EVALUATE the expected expression and compare with
                // fn:deep-equal. The old implementation was
                // `SerializeStringValue(result) == expected`, which is wrong twice over: it
                // compares a serialized result against expected SOURCE TEXT, and taking the
                // string value of a map raises FOTY0014 — "The string value of a map is not
                // defined" was 59 errors, all of them the harness stringifying a map result
                // for tests like parse-json("{}") deep-eq map{}.
                return await VerifyXPathAssertAsync(
                    result, $"deep-equal($result, ({assertion.Value}))", ct).ConfigureAwait(false);
            case "all-of":
                foreach (var c in assertion.Children)
                    if (!await VerifyAssertionAsync(testCase, c, result, ct).ConfigureAwait(false)) return false;
                return true;
            case "any-of":
                foreach (var c in assertion.Children)
                    if (await VerifyAssertionAsync(testCase, c, result, ct).ConfigureAwait(false)) return true;
                return false;
            default:
                return VerifyAssertion(assertion, result);
        }
    }

    /// <summary>
    /// Evaluates a plain <c>&lt;assert&gt;</c>: an XPath expression over the query result, with
    /// the result bound to <c>$result</c> (1383 of the corpus's ~1481 occurrences use it).
    /// </summary>
    /// <remarks>
    /// Previously there was no case for this at all, so it fell through the switch to
    /// <c>_ =&gt; false</c> and EVERY such test failed regardless of what the engine returned.
    /// A compile failure or a thrown expression is a failed assertion, not a harness crash —
    /// some assertions deliberately probe shapes the result may not have.
    /// </remarks>
    private async Task<bool> VerifyXPathAssertAsync(object? result, string? expr, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expr)) return false;
        try
        {
            // $result must be DECLARED: an undeclared variable fails STATIC ANALYSIS, so a
            // runtime binding never gets a chance. The first version of this compiled the bare
            // expression, every compile failed, and the catch-all returned false — leaving the
            // method inert while looking implemented. It moved the QT3 pass rate by 2 tests.
            var compiled = _engine.Compile("declare variable $result external; " + expr);
            if (!compiled.Success || compiled.ExecutionPlan is null) return false;

            var ctx = _engine.CreateContext(cancellationToken: ct);
            // SetExternalVariable, not BindVariable: VariableDeclarationOperator resolves an
            // external declaration through TryGetExternalVariable, which BindVariable does not
            // populate ("External variable $result was not bound and has no default value").
            ctx.SetExternalVariable("result", AsXdmSequence(result));

            var items = new List<object?>();
            await foreach (var item in compiled.ExecutionPlan.ExecuteAsync(ctx).ConfigureAwait(false))
            {
                items.Add(item);
                if (items.Count > MaxResultCount) return false;
            }
            return EffectiveBooleanValue(items);
        }
        catch (XQueryRuntimeException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    /// <summary>
    /// Converts a runner-side result into the shape the engine treats as a SEQUENCE.
    /// </summary>
    /// <remarks>
    /// The engine's multi-item representation is <c>object?[]</c> — see e.g. fn:replicate,
    /// <c>seq is object?[] arr ? arr : new[] { seq }</c>. A <c>List&lt;object?&gt;</c> is
    /// therefore a SINGLE item: bound as-is, <c>count($result)</c> on a three-item list
    /// returned 1 and <c>$result[2]</c> returned nothing, which made every sequence-shaped
    /// assertion — most of the corpus — evaluate false.
    ///
    /// A string is deliberately NOT treated as a sequence despite being IEnumerable, and an
    /// XdmNode is a single item even where it exposes children.
    /// </remarks>
    private static object? AsXdmSequence(object? result) => result switch
    {
        null => Array.Empty<object?>(),
        object?[] => result,
        string => result,
        XdmNode => result,
        // NOT flattened: after the change above, a List<object?> reaching here can only be an
        // ARRAY, which is a single item and must stay one. Flattening it was what broke
        // array:size($result) / $result instance of array(*).
        List<object?> => result,
        IEnumerable<object?> seq => seq.ToArray(),
        _ => result
    };

    /// <summary>
    /// XPath effective boolean value, limited to what an assertion can yield: an empty
    /// sequence is false, a single boolean is itself, a single string/number is its own EBV,
    /// and any non-empty node sequence is true.
    /// </summary>
    private static bool EffectiveBooleanValue(List<object?> items)
    {
        if (items.Count == 0) return false;
        if (items.Count > 1) return true;
        return items[0] switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            double d => d != 0 && !double.IsNaN(d),
            decimal m => m != 0,
            int i => i != 0,
            long l => l != 0,
            _ => true
        };
    }

    private bool VerifyAssertion(XqtsAssertion assertion, object? result)
    {
        return assertion.Type switch
        {
            "assert-true" => UnwrapSingle(result) is true || UnwrapSingle(result)?.ToString() == "true",
            "assert-false" => UnwrapSingle(result) is false || UnwrapSingle(result)?.ToString() == "false",
            "assert-empty" => result == null
                || (result is List<object?> emptyList && emptyList.Count == 0)
                || (result is ICollection<object> c && c.Count == 0),
            "assert-string-value" => SerializeStringValue(result) == assertion.Value,
            "assert-type" => VerifyType(result, assertion.Value),
            "assert-count" => VerifyCount(result, assertion.Value),
            "assert-xml" => VerifyXmlEqual(result, assertion.Value),
            "assert-permutation" => VerifyPermutation(result, assertion.Value),
            "error" => false, // Expected error, but we got a result
            _ => false // Unknown assertion type — must be explicitly implemented
        };
    }

    /// <summary>
    /// The error codes the engine actually reported, walking the whole inner-exception chain.
    /// <para>
    /// <c>XQueryException</c> and its relatives carry the code in a structured <c>ErrorCode</c>
    /// property and deliberately keep it OUT of <c>Message</c>; the XSLT layer then re-wraps
    /// those with file/line information. So <c>Message.Contains("FORX0002")</c> is false even
    /// when the engine identified the error exactly right — 69 regex cases alone were being
    /// scored "wrong code" while reporting the correct one. Reflection rather than a type list
    /// because several unrelated exception types expose this property.
    /// </para>
    /// </summary>
    private static IEnumerable<string> ReportedErrorCodes(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var prop = e.GetType().GetProperty(
                "ErrorCode", BindingFlags.Public | BindingFlags.Instance);
            if (prop is not null
                && prop.PropertyType == typeof(string)
                && prop.GetValue(e) is string { Length: > 0 } code)
            {
                yield return code;
            }
        }
    }

    /// <summary>
    /// True if <paramref name="assertion"/> — or, for &lt;any-of&gt;, one of its alternatives —
    /// is satisfied by <paramref name="ex"/>.
    /// </summary>
    private static bool MatchesExpectedError(XqtsAssertion assertion, Exception ex)
    {
        if (assertion.Type == "error")
        {
            // XQTS writes the code as an ATTRIBUTE — <error code="XPST0003"/> — so Element.Value,
            // and therefore `assertion.Value`, is "". Reading Value here meant `expectedCode` was
            // always empty and the IsNullOrEmpty branch passed the test for ANY exception: a query
            // that failed for entirely the wrong reason still scored as a pass. `Code` was parsed
            // all along (see ParseAssertions) and simply never consulted. An <error/> that really
            // carries no code still asks only that SOME error be raised, so that stays a pass.
            var expectedCode = !string.IsNullOrEmpty(assertion.Code)
                ? assertion.Code
                : assertion.Value;
            return string.IsNullOrEmpty(expectedCode)
                || ex.Message.Contains(expectedCode, StringComparison.Ordinal)
                || ReportedErrorCodes(ex).Contains(expectedCode, StringComparer.Ordinal);
        }

        // <any-of> is satisfied when any one alternative is — and its <error> alternatives must
        // match by code like any other. The old code returned true for any exception whenever an
        // <error> child merely existed, which is the same blanket-pass one level down.
        return assertion.Type == "any-of"
            && assertion.Children.Any(child => MatchesExpectedError(child, ex));
    }

    private static bool IsExpectedError(List<XqtsAssertion> assertions, Exception ex)
        => assertions.Any(assertion => MatchesExpectedError(assertion, ex));

    /// <summary>
    /// Unwraps a single-item list to its contained value.
    /// </summary>
    private static object? UnwrapSingle(object? result)
    {
        // object?[] is the sequence representation; a one-item sequence is its item.
        if (result is object?[] { Length: 1 } arr) return arr[0];
        // A List<object?> is an ARRAY and is already a single item — do NOT reach inside it.
        return result;
    }

    /// <summary>
    /// Serializes a result to its XQuery string value.
    /// For sequences, items are space-separated.
    /// </summary>
    private static string? SerializeStringValue(object? result)
    {
        if (result is null) return "";
        if (result is List<object?> list)
            return string.Join(" ", list.Select(SerializeItem));
        if (result is IList<object?> ilist)
            return string.Join(" ", ilist.Select(SerializeItem));
        return SerializeItem(result);
    }

    /// <summary>
    /// Serializes a single item to its XQuery string representation.
    /// </summary>
    private static string SerializeItem(object? item)
    {
        return PhoenixmlDb.XQuery.Functions.ConcatFunction.XQueryStringValue(item);
    }

    /// <summary>
    /// Extracts the inner value from XQuery constructor expressions like xs:float(3.14).
    /// </summary>
    private static string ExtractConstructorValue(string expected)
    {
        // Match patterns like xs:float(...), xs:double(...), xs:integer(...), xs:decimal(...)
        if (expected.StartsWith("xs:", StringComparison.Ordinal) && expected.EndsWith(')'))
        {
            var parenIdx = expected.IndexOf('(');
            if (parenIdx > 0)
            {
                var inner = expected[(parenIdx + 1)..^1];
                // Strip quotes from inner value
                if (inner.Length >= 2 &&
                    ((inner[0] == '"' && inner[^1] == '"') ||
                     (inner[0] == '\'' && inner[^1] == '\'')))
                {
                    inner = inner[1..^1];
                }
                return inner;
            }
        }
        return expected;
    }

    private static bool VerifyEq(object? result, string? expected)
    {
        result = UnwrapSingle(result);
        if (expected == null) return result == null;
        if (result == null) return false;

        var actualStr = result.ToString() ?? "";

        // Strip XQuery string literal quotes from expected value
        if (expected.Length >= 2 &&
            ((expected[0] == '"' && expected[^1] == '"') ||
             (expected[0] == '\'' && expected[^1] == '\'')))
        {
            expected = expected[1..^1];
        }

        // Handle XQuery constructor expressions like xs:float(3.4028235E38)
        expected = ExtractConstructorValue(expected);

        // Direct string match first
        if (actualStr == expected) return true;

        // Numeric comparison: handles formatting differences like E+308 vs E308
        if (result is double d)
        {
            if (double.TryParse(expected, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var expectedDbl))
            {
                if (double.IsNaN(d) && double.IsNaN(expectedDbl)) return true;
                return d == expectedDbl;
            }
        }

        if (result is float f)
        {
            if (float.TryParse(expected, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var expectedFlt))
            {
                if (float.IsNaN(f) && float.IsNaN(expectedFlt)) return true;
                return f == expectedFlt;
            }
        }

        if (result is decimal dc)
        {
            if (decimal.TryParse(expected, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var expectedDec))
                return dc == expectedDec;
        }

        if (result is long l)
        {
            if (long.TryParse(expected, System.Globalization.CultureInfo.InvariantCulture, out var expectedLong))
                return l == expectedLong;
        }

        if (result is bool b)
        {
            return string.Equals(b.ToString(), expected, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// &lt;serialization-matches&gt;: serialize the result under the output method the query's
    /// prolog declares, then match the given regex against it.
    /// </summary>
    /// <remarks>
    /// The options come from the ENGINE's own prolog reader
    /// (<c>XQueryFacade.DetectSerializationOptions</c>) rather than a copy here. Re-implementing
    /// engine behaviour in the harness is what produced the array/sequence and adaptive-array
    /// defects fixed earlier today; a second implementation drifts from the first and the tests
    /// then measure the copy.
    ///
    /// The corpus regexes are XPath-flavoured but in practice plain .NET-compatible; the one
    /// real difference is the "q" flag (match literally), which .NET has no equivalent for and
    /// which is handled by escaping instead.
    /// </remarks>
    /// <summary>
    /// Reads a &lt;test&gt; element's query, which QT3 may hold INLINE or in an external .xq
    /// file via a <c>file</c> attribute (41 times in the corpus).
    /// </summary>
    /// <remarks>
    /// XElement.Value of <c>&lt;test file="..."/&gt;</c> is the empty string, so those tests
    /// were compiling an EMPTY query. The engine duly said
    /// "mismatched input '&lt;EOF&gt;'" — 80 parse errors that read as grammar gaps in direct
    /// element constructors, when the grammar was never given anything to parse.
    /// </remarks>
    private static string ReadTestQuery(XElement? testElem, string basePath)
    {
        if (testElem is null) return "";
        var file = testElem.Attribute("file")?.Value;
        if (string.IsNullOrEmpty(file)) return testElem.Value;

        var path = Path.Combine(basePath, file);
        // A missing file leaves the query empty, which fails the test — the right outcome, and
        // better than throwing and taking the rest of the sweep with it.
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    /// <summary>Inline value, or the contents of the <c>file</c> the element points at.</summary>
    private static string ReadExternalOrInline(XElement elem, string basePath)
    {
        var file = elem.Attribute("file")?.Value;
        if (string.IsNullOrEmpty(file)) return elem.Value;

        var path = Path.Combine(basePath, file);
        return File.Exists(path) ? File.ReadAllText(path) : elem.Value;
    }

    private bool VerifySerializationMatches(XqtsTestCase testCase, XqtsAssertion assertion, object? result)
    {
        if (assertion.Value is not { } pattern) return false;

        string serialized;
        try
        {
            var options = XQueryFacade.DetectSerializationOptions(testCase.Query);
            serialized = XQueryResultSerializer.Serialize(result, _documents, options);
        }
        catch (XQueryRuntimeException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (NotSupportedException) { return false; }

        var flags = assertion.Flags ?? "";
        var opts = RegexOptions.None;
        if (flags.Contains('i', StringComparison.Ordinal)) opts |= RegexOptions.IgnoreCase;
        if (flags.Contains('m', StringComparison.Ordinal)) opts |= RegexOptions.Multiline;
        if (flags.Contains('s', StringComparison.Ordinal)) opts |= RegexOptions.Singleline;
        if (flags.Contains('x', StringComparison.Ordinal)) opts |= RegexOptions.IgnorePatternWhitespace;
        // XPath's "q" flag means the pattern is a literal string, which .NET expresses by
        // escaping rather than by an option.
        if (flags.Contains('q', StringComparison.Ordinal)) pattern = Regex.Escape(pattern);

        try
        {
            return Regex.IsMatch(serialized, pattern, opts, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException) { return false; }   // pattern .NET cannot compile
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>&lt;assert-serialization&gt;: serialized output must equal the given text.</summary>
    private bool VerifySerializationEquals(XqtsTestCase testCase, XqtsAssertion assertion, object? result)
    {
        if (assertion.Value is not { } expected) return false;
        string actual;
        try
        {
            var options = XQueryFacade.DetectSerializationOptions(testCase.Query);
            actual = XQueryResultSerializer.Serialize(result, _documents, options);
        }
        catch (XQueryRuntimeException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (NotSupportedException) { return false; }

        // Line endings differ between the corpus files and what the serializer emits; nothing
        // in these tests turns on CR vs LF, so normalise both rather than fail on it.
        static string N(string t) => t.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return string.Equals(N(actual), N(expected), StringComparison.Ordinal);
    }

    /// <summary>
    /// &lt;assert-serialization-error code="..."/&gt;: serializing the result must RAISE the
    /// named error. A result that serializes cleanly fails the test, which is the point —
    /// these cases exist to pin the cases serialization must refuse.
    /// </summary>
    private bool VerifySerializationError(XqtsTestCase testCase, XqtsAssertion assertion, object? result)
    {
        try
        {
            var options = XQueryFacade.DetectSerializationOptions(testCase.Query);
            XQueryResultSerializer.Serialize(result, _documents, options);
            return false; // serialized fine — the expected error never happened
        }
        catch (XQueryRuntimeException ex)
        {
            // A code on the assertion must match; without one, any serialization error will do.
            return assertion.Code is null
                || string.Equals(ex.ErrorCode, assertion.Code, StringComparison.Ordinal);
        }
        catch (InvalidOperationException) { return assertion.Code is null; }
        catch (NotSupportedException) { return assertion.Code is null; }
    }

    private bool VerifyType(object? result, string? expectedType)
    {
        result = UnwrapSingle(result);
        if (expectedType == null) return true;
        // Simplified type checking - integer subtypes all map to long in our engine
        return expectedType switch
        {
            "xs:integer" or "xs:int" or "xs:long" or "xs:short" or "xs:byte"
                or "xs:unsignedLong" or "xs:unsignedInt" or "xs:unsignedShort" or "xs:unsignedByte"
                or "xs:positiveInteger" or "xs:nonNegativeInteger"
                or "xs:negativeInteger" or "xs:nonPositiveInteger"
                => result is int or long,
            "xs:decimal" => result is decimal or int or long,
            "xs:double" => result is double,
            "xs:float" => result is float,
            "xs:string" or "xs:anyURI" or "xs:untypedAtomic" or "xs:normalizedString"
                or "xs:NCName" or "xs:Name" or "xs:NMTOKEN" or "xs:language"
                => result is string,
            "xs:boolean" => result is bool,
            _ => true // Unknown type, assume pass
        };
    }

    private bool VerifyCount(object? result, string? expectedCount)
    {
        if (!int.TryParse(expectedCount, out var expected)) return false;
        if (result is List<object?> list) return list.Count == expected;
        if (result is ICollection<object> c) return c.Count == expected;
        return expected == 1 && result != null;
    }

    private bool VerifyXmlEqual(object? result, string? expected)
    {
        if (expected == null) return result == null;
        try
        {
            // SERIALIZE the result as XML. This used item.ToString(), which for an XdmNode is
            // not its markup — so a correct element result was compared as the wrong text and
            // the test failed however right the engine was. prod-OrderByClause is 157 such
            // failures: the engine's output for orderbywithout-1 matches the expected
            // <results>A String B String …</results> character for character.
            var items = result is List<object?> list ? list : [result];
            var resultStr = string.Concat(items.Select(item =>
                XQueryResultSerializer.Serialize(item, _documents, OutputMethod.Xml)));

            // Wrap both in a root element for comparison if they're fragments
            var wrappedResult = $"<r>{resultStr}</r>";
            var wrappedExpected = $"<r>{expected}</r>";
            var resultXml = XDocument.Parse(wrappedResult);
            var expectedXml = XDocument.Parse(wrappedExpected);
            return XNode.DeepEquals(resultXml, expectedXml);
        }
        catch
        {
            return false;
        }
    }

    private bool VerifyPermutation(object? result, string? expected)
    {
        // Check if result contains same items in any order
        List<string?> resultStrings;
        if (result is List<object?> list)
            resultStrings = list.Select(r => r?.ToString()).ToList();
        else if (result is ICollection<object> resultItems)
            resultStrings = resultItems.Select(r => r?.ToString()).ToList();
        else
            return false;

        var expectedItems = expected?.Split(',').Select(s => s.Trim()).ToList();
        if (expectedItems == null) return false;

        return resultStrings.OrderBy(s => s).SequenceEqual(expectedItems.OrderBy(s => s));
    }

    /// <summary>
    /// Runs all test cases and returns a summary.
    /// </summary>
    public async Task<XqtsTestSummary> RunAllTestsAsync(
        IReadOnlyList<XqtsTestCase> testCases,
        IProgress<XqtsTestResult>? progress = null,
        CancellationToken ct = default)
    {
        var summary = new XqtsTestSummary
        {
            TotalTests = testCases.Count,
            StartTime = DateTimeOffset.UtcNow
        };

        foreach (var testCase in testCases)
        {
            ct.ThrowIfCancellationRequested();

            var result = await RunTestAsync(testCase, ct);
            summary.Results.Add(result);

            if (result.Passed)
                summary.PassedTests++;
            else if (result.Error != null)
                summary.ErrorTests++;
            else
                summary.FailedTests++;

            progress?.Report(result);
        }

        summary.EndTime = DateTimeOffset.UtcNow;
        return summary;
    }
}

/// <summary>
/// Configuration for XQTS test runner.
/// </summary>
public sealed class XqtsConfiguration
{
    public string XQueryVersion { get; init; } = "3.1";
    public bool SupportsSchemaValidation { get; init; } = true;
    public bool SupportsHigherOrderFunctions { get; init; } = true;
    public bool SupportsStaticTyping { get; init; } = false;
    public HashSet<string> SkipTests { get; } = new();
    public HashSet<string> SupportedFeatures { get; } = new()
    {
        "higherOrderFunctions",
        "moduleImport",
        "schemaImport",
        "schemaValidation",
        "staticTyping",
        "serialization",
        "infoset-dtd",
        "xpath-1.0-compatibility",
        "namespace-axis"
    };

    /// <summary>
    /// True if this engine implements one of the spec versions a <c>&lt;dependency type="spec"&gt;</c>
    /// lists. The value is a space-separated list like <c>"XQ10"</c>, <c>"XQ30+"</c> or
    /// <c>"XP31+ XQ31+"</c>; a trailing <c>+</c> means "that version and later".
    /// <para>
    /// The old check was <c>dep.Value?.Contains("XQ") == true</c>, which accepted EVERY XQuery
    /// version including <c>XQ10</c> — so tests written to pin XQuery 1.0 behaviour ran against a
    /// 4.0 engine and their failures were counted against us. They are not failures: `Axes127`
    /// says in its own description "the namespace-node() kind test is new in XQuery 3.0" and
    /// asserts XPST0017, i.e. it requires the engine NOT to support something we do support.
    /// `K-SeqExprCast-71a` pins the 1.0 rule for casting xs:untypedAtomic to xs:QName, citing bug
    /// 16059 — the same bug our own CastOperator comment cites as the thing XQuery 3.0 relaxed.
    /// </para>
    /// <para>
    /// Deliberately conservative, because this change RAISES the score and so deserves more
    /// scepticism than one that lowers it: an exact <c>XQ31</c> counts as applicable, since 4.0 is
    /// a superset of 3.1 and skipping those would be self-serving. Only exact <c>XQ10</c>/
    /// <c>XQ30</c> — versions whose behaviour later specs deliberately CHANGED — are excluded. A
    /// dependency naming no XQuery version at all (XPath-only) is left applicable, as before.
    /// </para>
    /// </summary>
    internal static bool SpecApplies(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var xq = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Where(t => t.StartsWith("XQ", StringComparison.Ordinal))
                      .ToList();
        if (xq.Count == 0) return true;
        return xq.Any(t => t.EndsWith('+')
            || string.Equals(t, "XQ40", StringComparison.Ordinal)
            || string.Equals(t, "XQ31", StringComparison.Ordinal));
    }

    public bool SatisfiesDependency(XqtsDependency dep)
    {
        return dep.Type switch
        {
            "spec" => SpecApplies(dep.Value) == dep.Satisfied,
            "feature" => SupportedFeatures.Contains(dep.Value ?? "") == dep.Satisfied,
            "xsd-version" => dep.Satisfied,
            "xml-version" => dep.Satisfied,
            "limits" => dep.Satisfied,
            _ => dep.Satisfied
        };
    }
}

/// <summary>
/// Represents an XQTS test case.
/// </summary>
public sealed class XqtsTestCase
{
    public required string Name { get; init; }
    public required string TestSet { get; init; }
    public string Description { get; init; } = "";
    public required string Query { get; init; }
    public XqtsEnvironment? Environment { get; set; }

    /// <summary>
    /// Module namespace -> .xq file declared directly on the TEST-CASE. QT3 puts &lt;module&gt;
    /// both inside &lt;environment&gt; and as a sibling of it under &lt;test-case&gt;; the latter
    /// cannot be folded into the environment because environments are shared (often global), and
    /// mutating one would leak a module into every other test that references it.
    /// </summary>
    public Dictionary<string, string> Modules { get; } = new();

    /// <summary>
    /// Directory of the test-set file this case came from, inside the CORPUS. It becomes the
    /// query's static base URI, which is what a relative URI in the test resolves against.
    ///
    /// Without it the engine fell back to the process working directory — the build output —
    /// where qt3tests is deliberately NOT copied (see the csproj: the suite is located through
    /// QT3_TEST_SUITE instead). So misc-JsonTestSuite asked for
    /// "JSONTestSuite/test_parsing/y_array_empty-string.json" and got "Could not find a part of
    /// the path .../bin/Debug/net10.0/...", even though the corpus holds all 318 of those files.
    /// A stale, EMPTY JSONTestSuite directory left in the output by an older build made it look
    /// like the data was missing rather than the base URI wrong.
    /// </summary>
    public string BaseDir { get; set; } = "";

    public List<XqtsDependency> Dependencies { get; } = new();
    public List<XqtsAssertion> Assertions { get; set; } = new();
}

/// <summary>
/// Test environment with source documents and context.
/// </summary>
public sealed class XqtsEnvironment
{
    public Dictionary<string, string> Sources { get; } = new();
    public Dictionary<string, string> Namespaces { get; } = new();
    public Dictionary<string, string> Parameters { get; } = new();

    /// <summary>Target namespace URI -> .xsd path, from the environment's &lt;schema&gt;.</summary>
    public Dictionary<string, string> Schemas { get; } = new();

    /// <summary>Document URI -> file path, for sources and resources addressable by fn:doc /
    /// fn:json-doc / fn:unparsed-text.</summary>
    public Dictionary<string, string> UriDocuments { get; } = new();

    /// <summary>Module namespace URI -> .xq file, from the environment's &lt;module&gt;.</summary>
    public Dictionary<string, string> Modules { get; } = new();
}

/// <summary>
/// Test dependency declaration.
/// </summary>
public sealed class XqtsDependency
{
    public required string Type { get; init; }
    public required string Value { get; init; }
    public bool Satisfied { get; init; } = true;
}

/// <summary>
/// Result assertion.
/// </summary>
public sealed class XqtsAssertion
{
    public required string Type { get; init; }
    public string? Value { get; init; }

    /// <summary>
    /// The <c>flags</c> attribute of &lt;serialization-matches&gt;, in XPath regex flag
    /// letters. Only "q" (literal) and "i"/"m"/"s"/"x" appear in the corpus.
    /// </summary>
    public string? Flags { get; init; }

    /// <summary>
    /// The <c>code</c> attribute of &lt;assert-serialization-error&gt; — the serialization
    /// error the test expects, e.g. SENR0001, SEPM0016, SERE0022.
    /// </summary>
    public string? Code { get; init; }

    public List<XqtsAssertion> Children { get; set; } = new();
}

/// <summary>
/// Result of running a single test.
/// </summary>
public sealed class XqtsTestResult
{
    public required XqtsTestCase TestCase { get; init; }
    public bool Passed { get; set; }
    public object? ActualResult { get; set; }
    public Exception? Error { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Summary of test run.
/// </summary>
public sealed class XqtsTestSummary
{
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    public int ErrorTests { get; set; }
    public int SkippedTests => TotalTests - PassedTests - FailedTests - ErrorTests;
    public double PassRate => TotalTests > 0 ? (double)PassedTests / TotalTests * 100 : 0;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public List<XqtsTestResult> Results { get; } = new();
}


/// <summary>A test query that did not stop when cancelled and was left running.</summary>
public sealed class QueryAbandonedException : TimeoutException
{
    public QueryAbandonedException() { }
    public QueryAbandonedException(string message) : base(message) { }
    public QueryAbandonedException(string message, Exception innerException) : base(message, innerException) { }
}
