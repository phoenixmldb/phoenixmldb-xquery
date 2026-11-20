using BenchmarkDotNet.Attributes;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Benchmarks;

/// <summary>
/// The cost of evaluating one XPath expression with each element of a document as the context
/// item, as a validator's rules do. The document is parsed and the expression compiled once in
/// <see cref="Setup"/>; the benchmark evaluates the expression for every element with one reused
/// context, and the result is reported per element (OperationsPerInvoke).
/// </summary>
public class XPathPerNodeBenchmarks
{
    private QueryEngine _engine = null!;
    private QueryCompilationResult _query = null!;
    private QueryExecutionContext _context = null!;
    private XdmElement[] _elements = null!;

    [Params("large", "large-ns", "wide", "deep")]
    public string Document { get; set; } = "large";

    [Params(
        "child-name", "attr-eq-predicate", "position-1", "following-sibling-1", "ancestor-with-attr",
        "count-descendants", "attr-in-sequence", "not-attr", "exists-child", "normalize-space",
        "matches", "compare")]
    public string Expression { get; set; } = "child-name";

    private static string Xpath(string name, bool namespaced) => name switch
    {
        "child-name" => namespaced ? "b:e1" : "e1",
        "attr-eq-predicate" => "*[@a = 'v1']",
        "position-1" => "*[1]",
        "following-sibling-1" => "following-sibling::*[1]",
        "ancestor-with-attr" => "ancestor::*[@n]",
        "count-descendants" => "count(descendant::*)",
        "attr-in-sequence" => "@a = ('v1', 'v2', 'v3')",
        "not-attr" => "not(@ref)",
        "exists-child" => "exists(child::*)",
        "normalize-space" => "string-length(normalize-space(@a)) gt 0",
        "matches" => "matches(@a, '^v[0-9]+$')",
        "compare" => "compare(@a, 'v500') lt 0",
        _ => throw new ArgumentException(name),
    };

    public int Elements => _elements.Length;

    [GlobalSetup]
    public void Setup()
    {
        var namespaced = Document == "large-ns";
        var xml = Document switch
        {
            "large" or "large-ns" => XPathDocuments.Large(namespaced),
            "wide" => XPathDocuments.Wide(),
            _ => XPathDocuments.Deep(),
        };
        var store = new XdmDocumentStore();
        var document = store.LoadFromString(xml);
        _engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var prolog = namespaced ? $"declare namespace b = '{XPathDocuments.Namespace}'; " : "";
        _query = _engine.Compile(prolog + Xpath(Expression, namespaced));
        if (!_query.Success)
            throw new InvalidOperationException(string.Join("; ", _query.Errors.Select(e => e.Message)));
        _context = _engine.CreateContext();
        var elements = new List<XdmElement>();
        Collect(store, document, elements);
        _elements = [.. elements];
    }

    private static void Collect(XdmDocumentStore store, XdmNode node, List<XdmElement> into)
    {
        if (node is XdmElement e) into.Add(e);
        IEnumerable<PhoenixmlDb.Core.NodeId> children = node switch
        {
            XdmDocument d => d.Children,
            XdmElement el => el.Children,
            _ => [],
        };
        foreach (var id in children)
            if (store.GetNode(id) is { } child)
                Collect(store, child, into);
    }

    [Benchmark]
    public async Task<int> PerElement()
    {
        var items = 0;
        foreach (var element in _elements)
        {
            _context.PushContextItem(element);
            await foreach (var _ in _query.ExecutionPlan!.ExecuteAsync(_context))
                items++;
            _context.PopContextItem();
        }
        return items;
    }
}
