using System.Text;
using System.Xml.Linq;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.FullText;
using PhoenixmlDb.XQuery.Parser;

namespace PhoenixmlDb.Conformance.Tests.FullText;

/// <summary>The verdict of one XQFTTS case.</summary>
/// <param name="Group">The case's FilePath without its trailing slash, e.g. "Examples/3.4.3-ThesaurusOption".</param>
/// <param name="Name">The case name.</param>
/// <param name="Outcome">"pass", "fail", or "inspect" (the catalog asks for a human comparison and the automatic one did not match).</param>
/// <param name="Detail">Why it failed: the error raised, or the result and the first expected one.</param>
public sealed record XqfttsResult(string Group, string Name, string Outcome, string Detail);

/// <summary>
/// Runs the W3C XQuery and XPath Full Text 1.0 test suite (XQFTTS 1.0.4) against this engine.
/// </summary>
/// <remarks>
/// <para>
/// QT3 has no full-text coverage; this is the suite for <c>contains text</c>. It is fetched by
/// <c>scripts/fetch-fulltext-suite.sh</c> (a pinned, checksummed zip, never committed) and
/// gated per case by <c>scripts/fulltext-conformance.sh</c>.
/// </para>
/// <para>
/// Verdicts: a case that expects an error passes only when an expected code is raised (a parse
/// failure is XPST0003); a case that expects output passes when the serialized result equals one
/// of the alternative expected files under the catalog's comparison (XML, Fragment and Text all
/// compare as a fragment wrapped in one element, whitespace-only text dropped and other text
/// space-normalized; Ignore needs only a result). A case marked Inspect that does not match
/// automatically is "inspect", not "pass": a human must rule on it.
/// </para>
/// </remarks>
public sealed class XqfttsRunner
{
    private static readonly XNamespace Ns = "http://www.w3.org/2005/02/query-test-full-text";
    private readonly string _suite;
    private readonly XDocument _catalog;
    private readonly Dictionary<string, string> _sources;
    private readonly List<(string Kind, string Id, string Uri, string File)> _resources;

    public XqfttsRunner(string suiteDirectory)
    {
        _suite = suiteDirectory;
        _catalog = XDocument.Load(Path.Combine(suiteDirectory, "XQFTTSCatalog.xml"));
        _sources = _catalog.Descendants(Ns + "source")
            .ToDictionary(s => (string)s.Attribute("ID")!, s => Path.Combine(suiteDirectory, (string)s.Attribute("FileName")!));
        _resources = _catalog.Descendants()
            .Where(e => e.Name == Ns + "stopwords" || e.Name == Ns + "thesaurus")
            .Select(e => (e.Name.LocalName, (string)e.Attribute("ID")!, (string)e.Attribute("uri")!,
                Path.Combine(suiteDirectory, (string)e.Attribute("FileName")!)))
            .ToList();
    }

    public int CaseCount => _catalog.Descendants(Ns + "test-case").Count();

    public async Task<List<XqfttsResult>> RunAllAsync(TimeSpan perCase)
    {
        var results = new List<XqfttsResult>();
        foreach (var testCase in _catalog.Descendants(Ns + "test-case"))
            results.Add(await RunAsync(testCase, perCase));
        return results;
    }

    private async Task<XqfttsResult> RunAsync(XElement testCase, TimeSpan perCase)
    {
        var name = (string)testCase.Attribute("name")!;
        var path = (string)testCase.Attribute("FilePath")!;
        var group = path.TrimEnd('/');
        var errors = testCase.Elements(Ns + "expected-error").Select(e => e.Value.Trim()).ToList();
        var outputs = testCase.Elements(Ns + "output-file").ToList();
        XqfttsResult Result(string outcome, string detail) => new(group, name, outcome, Flat(detail, 300));

        string query;
        string queryFile;
        try
        {
            var queryName = (string)testCase.Element(Ns + "query")!.Attribute("name")!;
            queryFile = Path.GetFullPath(Path.Combine(_suite, "Queries", "XQuery", path, queryName + ".xq"));
            query = await File.ReadAllTextAsync(queryFile);
        }
        catch (IOException e)
        {
            return Result("fail", "harness: query file: " + e.Message);
        }

        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store) { FullTextResources = Resources(testCase) };

        QueryCompilationResult compiled;
        try
        {
            compiled = engine.Compile(query);
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
            using var cts = new CancellationTokenSource(perCase);
            // <contextItem> names a source that is the initial context item (ft-3.2-examples-*).
            object? contextItem = null;
            if (testCase.Element(Ns + "contextItem") is { } contextSource)
                contextItem = await LoadAsync(contextSource.Value.Trim());
            using var context = engine.CreateContext(initialContextItem: contextItem, cancellationToken: cts.Token);
            // Relative URIs in a query (the Catalog cases' doc("../../../XQFTTSCatalog.xml"))
            // are written against the query file.
            context.StaticBaseUri = new Uri(queryFile).AbsoluteUri;
            foreach (var input in testCase.Elements(Ns + "input-file"))
                context.SetExternalVariable((string)input.Attribute("variable")!, await LoadAsync(input.Value.Trim()));

            async Task<object> LoadAsync(string sourceId)
            {
                var file = Path.GetFullPath(_sources[sourceId]);
                return store.LoadFromString(await File.ReadAllTextAsync(file, cts.Token), new Uri(file).AbsoluteUri);
            }
            await foreach (var item in compiled.ExecutionPlan.ExecuteAsync(context).WithCancellation(cts.Token))
                items.Add(item);
        }
        catch (OperationCanceledException)
        {
            return Result("fail", $"timeout after {perCase.TotalSeconds:0} s");
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
            return Result("fail", "no error; expected " + string.Join("|", errors));

        string actual;
        try
        {
            actual = await SerializeAsync(engine, items);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Result("fail", "serialize: " + e.Message);
        }

        var inspect = false;
        string? firstExpected = null;
        foreach (var output in outputs)
        {
            var compare = (string?)output.Attribute("compare") ?? "XML";
            if (compare == "Ignore")
                return Result("pass", "");
            inspect |= compare == "Inspect";
            var expected = await File.ReadAllTextAsync(Path.Combine(_suite, "ExpectedTestResults", path, output.Value.Trim()));
            firstExpected ??= expected;
            if (SameFragment(expected, actual))
                return Result("pass", "");
        }
        // XQFTTS_DUMP: a directory to write the full result and first expected output of each
        // case that did not match, for comparing by hand.
        if (Environment.GetEnvironmentVariable("XQFTTS_DUMP") is { Length: > 0 } dump)
        {
            Directory.CreateDirectory(dump);
            await File.WriteAllTextAsync(Path.Combine(dump, name + ".actual"), actual);
            await File.WriteAllTextAsync(Path.Combine(dump, name + ".expected"), firstExpected ?? "");
        }
        return Result(inspect ? "inspect" : "fail",
            (errors.Count > 0 ? "no error (an error was also allowed); " : "")
            + "got: " + Flat(actual, 140) + " | want: " + Flat(firstExpected ?? "", 140));

        XqfttsResult Error(string code, string message)
        {
            var codes = errors.SelectMany(e => e.Split('|')).Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
            var matched = codes.Any(c => code.Contains(c, StringComparison.Ordinal) || message.Contains(c, StringComparison.Ordinal));
            return Result(matched ? "pass" : "fail",
                $"error {code}" + (codes.Count > 0 ? $" (expected {string.Join("|", codes)})" : "") + ": " + message);
        }
    }

    /// <summary>
    /// The catalog's stop word lists and thesauri (its &lt;stopwords&gt; and &lt;thesaurus&gt;
    /// entries), by URI: the statically known resources. An entry whose URI is "##default" is the
    /// default list or thesaurus for the cases that name it in an &lt;aux-URI&gt;. A file the
    /// catalog marks missing ("intentionally-missing") is not registered, so the case sees FTST0008
    /// or FTST0018.
    /// </summary>
    private FullTextResources Resources(XElement testCase)
    {
        var resources = new FullTextResources();
        var named = testCase.Elements(Ns + "aux-URI").Select(a => a.Value.Trim()).ToHashSet(StringComparer.Ordinal);
        foreach (var (kind, id, uri, file) in _resources)
        {
            if (!File.Exists(file))
                continue;
            var isDefault = uri == "##default";
            if (isDefault && !named.Contains(id))
                continue;
            if (kind == "stopwords")
            {
                var words = FullTextResources.ParseStopWords(File.ReadAllText(file));
                if (isDefault) resources.StopWordsByDefault = words;
                else resources.AddStopWords(uri, words);
            }
            else
            {
                var thesaurus = FullTextThesaurus.Load(XDocument.Load(file));
                if (isDefault) resources.ThesaurusByDefault = thesaurus;
                else resources.AddThesaurus(uri, thesaurus);
            }
        }
        return resources;
    }

    private static async Task<string> SerializeAsync(QueryEngine engine, List<object?> items)
    {
        var compiled = engine.Compile(
            "declare variable $result external; serialize($result, map{'method':'xml','omit-xml-declaration':true()})");
        using var context = engine.CreateContext();
        context.SetExternalVariable("result", items.ToArray());
        var text = new StringBuilder();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(context))
            text.Append(item);
        return text.ToString();
    }

    internal static bool SameFragment(string expected, string actual)
    {
        var e = Parse(expected);
        var a = Parse(actual);
        if (e is null || a is null)
            return NormalizeSpace(expected) == NormalizeSpace(actual);
        Clean(e);
        Clean(a);
        return XNode.DeepEquals(e, a);

        static XElement? Parse(string fragment)
        {
            try { return XElement.Parse("<fragment>" + fragment + "</fragment>"); }
            catch (System.Xml.XmlException) { return null; }
        }

        static void Clean(XElement element)
        {
            foreach (var text in element.DescendantNodes().OfType<XText>().ToList())
            {
                var normalized = NormalizeSpace(text.Value);
                if (normalized.Length == 0) text.Remove();
                else text.Value = normalized;
            }
            foreach (var comment in element.DescendantNodes().OfType<XComment>().ToList())
                comment.Remove();
            foreach (var e in element.DescendantsAndSelf())
            {
                // <a/> and <a></a> are the same element; XNode.DeepEquals compares IsEmpty.
                if (!e.Nodes().Any())
                    e.RemoveNodes();
                var attributes = e.Attributes().Where(x => !x.IsNamespaceDeclaration).OrderBy(x => x.Name.ToString()).ToList();
                e.ReplaceAttributes(attributes);
            }
        }
    }

    private static string NormalizeSpace(string s)
        => string.Join(' ', s.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static string Flat(string s, int max)
    {
        s = NormalizeSpace(s);
        return s.Length > max ? s[..max] + "…" : s;
    }
}
