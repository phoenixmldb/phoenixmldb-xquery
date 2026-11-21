using System.Text.RegularExpressions;
using System.Xml.Linq;
using PhoenixmlDb.Conformance.Tests.FullText;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;

namespace PhoenixmlDb.Conformance.Tests.Update;

/// <summary>The verdict of one XQUTS case.</summary>
/// <param name="Group">The case's FilePath without its trailing slash, e.g. "InsertExpressions".</param>
/// <param name="Name">The case name.</param>
/// <param name="Outcome">"pass", "fail", or "inspect" (the catalog asks for a human comparison and the automatic one did not match).</param>
/// <param name="Detail">Why it failed: the state, and the error raised or the result and the first expected one.</param>
public sealed record XqutsResult(string Group, string Name, string Outcome, string Detail);

/// <summary>
/// Runs the W3C XQuery Update Facility 1.0 test suite (XQUTS 1.0.1) against this engine.
/// </summary>
/// <remarks>
/// <para>
/// The suite is fetched by <c>scripts/fetch-xquts-suite.sh</c> (a pinned, checksummed zip, never
/// committed) and gated per case by <c>scripts/xquts-conformance.sh</c>.
/// </para>
/// <para>
/// A case is a sequence of states run in <c>time</c> order against the same documents: an
/// updating query in one state changes the document a later state reads. Each case gets its own
/// <see cref="XdmDocumentStore"/> and <see cref="QueryEngine"/>, which live across its states, and
/// every query runs through <see cref="QueryEngine.ExecuteAsync(ExecutionPlan, PhoenixmlDb.Core.ContainerId, object?, CancellationToken)"/>,
/// the path that applies a query's top-level pending update list. A case passes only when every
/// state passes (the suite's "Guidelines for Running"): a state with no expected result or error
/// passes when it raises no error, one with expected results when the result matches one of
/// them, one with expected errors when one of the codes is raised (a parse failure is XPST0003,
/// "*" is any error), one with both when either holds.
/// </para>
/// <para>
/// Binding the inputs. That execution path takes no external-variable bindings, so the harness
/// applies the suite's documented "collection function" customization: the declaration of each
/// variable a state binds, <c>declare variable $v [as T] external;</c>, gets the default value
/// <c>fn:collection("urn:xquts:source:ID")</c>, a collection that holds the case's one document
/// for source ID. The declaration stays external and typed, and every state sees the same
/// document node. An <c>input-URI</c> variable gets the URI string instead: the putOutput sources
/// are files that "do not exist by design" (fn:put writes them), so the URI names a file in a
/// per-case temporary directory, deleted after the case, and the suite directory stays read-only.
/// </para>
/// <para>
/// Sources that name a schema in the catalog (books, books2) are loaded schema-validated (strict)
/// against it, and every catalog schema is known to <c>import schema</c>; a source that fails to
/// validate is loaded untyped, so the query fails on its own terms.
/// </para>
/// <para>
/// Comparison is XQFTTS's (<see cref="XqfttsRunner.SameFragment"/>): XML, Fragment and Text all
/// compare as a fragment wrapped in one element, whitespace-only text dropped and other text
/// space-normalized; Ignore needs only a result; an Inspect state that does not match is
/// "inspect", not "pass".
/// </para>
/// <para>
/// Cases in a test group with a <c>depends-on</c> feature (revalidation modes, static typing,
/// full axis, fn:put node kinds) are run like every other case; the detail of each one that does
/// not pass starts with the feature, so a failure can be told from an unsupported feature.
/// </para>
/// <para>
/// Until the engine's in-memory store applies updates (#127), the engine raises XUST0002 "Update
/// expressions require an IUpdatableNodeStore-capable node provider" after an updating query.
/// That is never taken as the XQUF static error XUST0002 a state may expect: it is reported as
/// "updates not applied".
/// </para>
/// </remarks>
public sealed partial class XqutsRunner
{
    private static readonly XNamespace Ns = "http://www.w3.org/2005/02/query-test-update";
    private const string SourceCollection = "urn:xquts:source:";
    private const string NotUpdatableMarker = "IUpdatableNodeStore";
    private readonly string _suite;
    private readonly XDocument _catalog;
    private readonly Dictionary<string, (string File, string? Schema)> _sources;
    private readonly XsdSchemaProvider _schemas = new();
    private readonly Dictionary<string, string> _schemaNamespaces;

    public XqutsRunner(string suiteDirectory)
    {
        _suite = suiteDirectory;
        _catalog = XDocument.Load(Path.Combine(suiteDirectory, "XQUTSCatalog.xml"));
        var sources = _catalog.Root!.Element(Ns + "sources")!;
        _sources = sources.Elements(Ns + "source").ToDictionary(
            s => (string)s.Attribute("ID")!,
            s => (Path.GetFullPath(Path.Combine(suiteDirectory, (string)s.Attribute("FileName")!)), (string?)s.Attribute("schema")));
        _schemaNamespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var schema in sources.Elements(Ns + "schema"))
        {
            var uri = (string)schema.Attribute("uri")!;
            var file = Path.GetFullPath(Path.Combine(suiteDirectory, (string)schema.Attribute("FileName")!));
            _schemaNamespaces[(string)schema.Attribute("ID")!] = uri;
            if (!File.Exists(file))
                continue;
            // A schema this engine cannot compile surfaces as the case's own error.
            try { _schemas.ImportSchema(uri, [file]); }
            catch (SchemaException) { }
        }
    }

    public int CaseCount => _catalog.Descendants(Ns + "test-case").Count();

    public int StateCount => _catalog.Descendants(Ns + "state").Count();

    /// <summary>How many states a case has, by case name.</summary>
    public Dictionary<string, int> StatesPerCase => _catalog.Descendants(Ns + "test-case")
        .ToDictionary(c => (string)c.Attribute("name")!, c => c.Elements(Ns + "state").Count(), StringComparer.Ordinal);

    public async Task<List<XqutsResult>> RunAllAsync(TimeSpan perState)
    {
        var results = new List<XqutsResult>();
        foreach (var testCase in _catalog.Descendants(Ns + "test-case"))
            results.Add(await RunAsync(testCase, perState));
        return results;
    }

    private async Task<XqutsResult> RunAsync(XElement testCase, TimeSpan perState)
    {
        var name = (string)testCase.Attribute("name")!;
        var path = (string)testCase.Attribute("FilePath")!;
        var group = path.TrimEnd('/');
        var states = testCase.Elements(Ns + "state")
            .OrderBy(s => int.Parse((string)s.Attribute("time")!, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var features = DependsOn(testCase);
        XqutsResult Result(string outcome, string detail)
            => new(group, name, outcome,
                Flat((outcome == "pass" || features.Length == 0 ? "" : $"[depends-on {features}] ") + detail, 300));

        // One store and one engine for the whole case: a later state reads what an earlier one changed.
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: _schemas);
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        var putDirectory = Path.Combine(Path.GetTempPath(), "xquts-put-" + Guid.NewGuid().ToString("N"));
        try
        {
            var inspect = "";
            foreach (var state in states)
            {
                var verdict = await RunStateAsync(state, path, engine, store, loaded, putDirectory, perState);
                if (verdict.Outcome == "fail")
                    return Result("fail", verdict.Detail);
                if (verdict.Outcome == "inspect" && inspect.Length == 0)
                    inspect = verdict.Detail;
            }
            return inspect.Length > 0 ? Result("inspect", inspect) : Result("pass", "");
        }
        finally
        {
            // The suite: "A test harness should delete documents written to these locations after
            // each test case has been run."
            try { if (Directory.Exists(putDirectory)) Directory.Delete(putDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<(string Outcome, string Detail)> RunStateAsync(
        XElement state, string path, QueryEngine engine, XdmDocumentStore store, HashSet<string> loaded,
        string putDirectory, TimeSpan perState)
    {
        var queryName = (string)state.Element(Ns + "query")!.Attribute("name")!;
        var label = $"state {(string)state.Attribute("time")!} ({queryName})";
        var errors = state.Elements(Ns + "expected-error").Select(e => e.Value.Trim()).ToList();
        var outputs = state.Elements(Ns + "output-file").ToList();
        (string, string) Verdict(string outcome, string detail) => (outcome, outcome == "pass" ? "" : label + ": " + detail);

        string query;
        string queryFile;
        try
        {
            queryFile = Path.GetFullPath(Path.Combine(_suite, "Queries", "XQuery", path, queryName + ".xq"));
            query = await File.ReadAllTextAsync(queryFile);
        }
        catch (IOException e)
        {
            return Verdict("fail", "harness: query file: " + e.Message);
        }

        // Bind the state's inputs (see the class remarks).
        try
        {
            foreach (var input in state.Elements(Ns + "input-file"))
            {
                var id = input.Value.Trim();
                if (loaded.Add(id))
                    store.RegisterCollection(SourceCollection + id, [Load(id, store)]);
                query = WithDefault(query, (string)input.Attribute("variable")!, $"fn:collection(\"{SourceCollection}{id}\")");
            }
            foreach (var input in state.Elements(Ns + "input-URI"))
            {
                Directory.CreateDirectory(putDirectory);
                var id = input.Value.Trim();
                var file = Path.Combine(putDirectory, Path.GetFileName(_sources[id].File));
                query = WithDefault(query, (string)input.Attribute("variable")!, $"\"{new Uri(file).AbsoluteUri}\"");
            }
        }
        catch (Exception e) when (e is IOException or KeyNotFoundException or System.Xml.XmlException)
        {
            return Verdict("fail", "harness: source: " + e.Message);
        }

        QueryCompilationResult compiled;
        try
        {
            compiled = engine.Compile(query, new CompilationOptions { BaseUri = new Uri(queryFile).AbsoluteUri });
        }
        catch (XQueryParseException e)
        {
            return Error("XPST0003", e.Message);
        }
        if (!compiled.Success || compiled.ExecutionPlan is null)
        {
            return Error(
                string.Join(",", compiled.Errors.Select(e => e.Code)),
                string.Join("; ", compiled.Errors.Select(e => e.Message)));
        }

        var items = new List<object?>();
        try
        {
            using var cts = new CancellationTokenSource(perState);
            // The path that applies the query's top-level pending update list once the results are read.
            await foreach (var item in engine.ExecuteAsync(compiled.ExecutionPlan, cancellationToken: cts.Token).WithCancellation(cts.Token))
                items.Add(item);
        }
        catch (OperationCanceledException)
        {
            return Verdict("fail", $"timeout after {perState.TotalSeconds:0} s");
        }
        catch (XQueryRuntimeException e) when (e.Message.Contains(NotUpdatableMarker, StringComparison.Ordinal))
        {
            return Verdict("fail", $"updates not applied (#127): {e.ErrorCode}: {e.Message}"
                + (errors.Count > 0 ? $" (expected {string.Join("|", errors)})" : ""));
        }
        catch (XQueryRuntimeException e)
        {
            return Error(e.ErrorCode, e.Message);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Error("", e.GetType().Name + ": " + e.Message);
        }

        if (outputs.Count == 0)
        {
            return errors.Count == 0
                ? Verdict("pass", "")
                : Verdict("fail", "no error; expected " + string.Join("|", errors));
        }

        string actual;
        try
        {
            actual = await XqfttsRunner.SerializeAsync(engine, items);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Verdict("fail", "serialize: " + e.Message);
        }

        var inspect = false;
        string? firstExpected = null;
        foreach (var output in outputs)
        {
            var compare = (string?)output.Attribute("compare") ?? "XML";
            if (compare == "Ignore")
                return Verdict("pass", "");
            inspect |= compare == "Inspect";
            var expected = await File.ReadAllTextAsync(Path.Combine(_suite, "ExpectedTestResults", path, output.Value.Trim()));
            firstExpected ??= expected;
            if (XqfttsRunner.SameFragment(WithoutXmlDeclaration(expected), actual))
                return Verdict("pass", "");
        }
        // XQUTS_DUMP: a directory to write the full result and first expected output of each
        // state that did not match, for comparing by hand.
        if (Environment.GetEnvironmentVariable("XQUTS_DUMP") is { Length: > 0 } dump)
        {
            Directory.CreateDirectory(dump);
            await File.WriteAllTextAsync(Path.Combine(dump, queryName + ".actual"), actual);
            await File.WriteAllTextAsync(Path.Combine(dump, queryName + ".expected"), firstExpected ?? "");
        }
        return Verdict(inspect ? "inspect" : "fail",
            (errors.Count > 0 ? "no error (an error was also allowed); " : "")
            + "got: " + Flat(actual, 140) + " | want: " + Flat(firstExpected ?? "", 140));

        (string, string) Error(string code, string message)
        {
            var codes = errors.SelectMany(e => e.Split('|')).Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
            var matched = codes.Any(c => c == "*" || code.Contains(c, StringComparison.Ordinal) || message.Contains(c, StringComparison.Ordinal));
            return Verdict(matched ? "pass" : "fail",
                $"error {code}" + (codes.Count > 0 ? $" (expected {string.Join("|", codes)})" : "") + ": " + message);
        }
    }

    /// <summary>
    /// Loads source <paramref name="id"/> into the case's store: schema-validated (strict) when the
    /// catalog names a schema for it, else as parsed. Its URI is its file's.
    /// </summary>
    private object Load(string id, XdmDocumentStore store)
    {
        var (file, schema) = _sources[id];
        var text = ReadXml(File.ReadAllBytes(file));
        var uri = new Uri(file).AbsoluteUri;
        if (schema is not null && _schemaNamespaces.ContainsKey(schema))
        {
            try
            {
                if (_schemas.ValidateAndAnnotate(text, store, ValidationMode.Strict, null, null, uri) is { } annotated)
                    return annotated;
            }
            catch (SchemaException)
            {
                // Falls through to the untyped document.
            }
        }
        return store.LoadFromString(text, uri);
    }

    /// <summary>
    /// Decodes an XML file as its byte order mark or XML declaration says: ten sources are
    /// ISO-8859-1 (soapMsg.xml has non-ASCII names), and reading them as UTF-8 garbles them.
    /// </summary>
    internal static string ReadXml(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        reader.Peek();
        if (reader.CurrentEncoding is System.Text.UTF8Encoding
            && EncodingDeclaration().Match(System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 200))) is { Success: true } declared
            && !declared.Groups[1].Value.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
        {
            return System.Text.Encoding.GetEncoding(declared.Groups[1].Value).GetString(bytes);
        }
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^<\?xml\s[^?]*\bencoding\s*=\s*[""']([A-Za-z0-9._\-]+)[""']")]
    private static partial Regex EncodingDeclaration();

    /// <summary>
    /// Gives the external declaration of <paramref name="variable"/> the default value
    /// <paramref name="value"/>. A query that does not declare the variable is left alone: it
    /// does not read it, or reading it is the query's own error.
    /// </summary>
    internal static string WithDefault(string query, string variable, string value)
    {
        var declaration = new Regex(
            @"(declare\s+variable\s+\$" + Regex.Escape(variable) + @"(?![\w.\-])[^;]*?\bexternal)\s*;",
            RegexOptions.CultureInvariant);
        return declaration.Replace(query, m => m.Groups[1].Value + " := " + value + ";", 1);
    }

    /// <summary>
    /// Drops a leading XML declaration: six expected files (id-delete-expr-017, -018, ...) start
    /// with one, which canonical XML has no place for and which cannot sit inside the wrapping
    /// element <see cref="XqfttsRunner.SameFragment"/> adds.
    /// </summary>
    internal static string WithoutXmlDeclaration(string expected)
        => XmlDeclaration().Replace(expected, "", 1);

    [GeneratedRegex(@"^\uFEFF?\s*<\?xml\s[^?]*\?>")]
    private static partial Regex XmlDeclaration();

    /// <summary>The depends-on features of the case's enclosing test groups, e.g. "revalidation:strict=true".</summary>
    private static string DependsOn(XElement testCase)
        => string.Join(", ", testCase.Ancestors(Ns + "test-group")
            .Reverse()
            .SelectMany(g => g.Elements(Ns + "GroupInfo").Elements(Ns + "depends-on").Elements(Ns + "feature"))
            .Select(f => f.Value.Trim() + "=" + ((string?)f.Attribute("supported") ?? "true")));

    private static string Flat(string s, int max)
    {
        s = string.Join(' ', s.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return s.Length > max ? s[..max] + "…" : s;
    }
}
