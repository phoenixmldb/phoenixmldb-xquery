using AwesomeAssertions;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A node of another store, passed into a query, is read in its own store (xslt BUGS.md #124).
/// </summary>
/// <remarks>
/// <para>
/// Node ids and namespace ids mean something only in the store that assigned them. A node passed
/// in from another store used to be navigated and named through the query's store, so its
/// children were whatever nodes had the same ids there, and its namespace was whatever URI had
/// the same id there: silently wrong data, no error.
/// </para>
/// <para>
/// The two stores below are built so that the same ids mean different things in each: the
/// query's store holds a document of its own with overlapping node ids, and each store interned a
/// different URI first, so namespace id 100 (the first user id) is a different namespace in each.
/// </para>
/// </remarks>
public class CrossStoreNodeTests
{
    private const string ForeignXml = """<r xmlns:p="urn:p"><p:a k="1"><b>t</b></p:a><c/></r>""";

    private static (XdmDocumentStore Query, XdmDocument Local, XdmDocument Foreign) Stores()
    {
        var foreignStore = new XdmDocumentStore();
        foreignStore.LoadFromString("""<x xmlns="urn:first-in-foreign"/>""");
        var foreign = foreignStore.LoadFromString(ForeignXml);

        var queryStore = new XdmDocumentStore();
        queryStore.LoadFromString("""<y xmlns="urn:first-in-query"/>""");
        var local = queryStore.LoadFromString("""<r><z>1</z><z>2</z><z>3</z></r>""");
        return (queryStore, local, foreign);
    }

    private static async Task<List<object?>> RunAsync(XdmDocumentStore store, string query, params (string Name, object? Value)[] bindings)
    {
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compilation = engine.Compile(query);
        compilation.Success.Should().BeTrue(string.Join("; ", compilation.Errors.Select(e => e.Message)));
        var context = engine.CreateContext();
        foreach (var (name, value) in bindings)
            context.SetExternalVariable(name, value);
        var results = new List<object?>();
        await foreach (var item in compilation.ExecutionPlan!.ExecuteAsync(context))
            results.Add(item);
        return results;
    }

    private static async Task<string> TextAsync(XdmDocumentStore store, string query, params (string Name, object? Value)[] bindings)
        => string.Concat((await RunAsync(store, query, bindings)).Select(i => i?.ToString()));

    [Fact]
    public async Task Foreign_document_is_navigated_and_named_in_its_own_store()
    {
        var (store, local, foreign) = Stores();
        var text = await TextAsync(store, """
            declare namespace p = "urn:p";
            declare variable $d external;
            declare variable $l external;
            string-join((
              count($d/r/*), name($d/r/*[1]), namespace-uri($d/r/*[1]), string($d/r/*[1]/b),
              string($d/r/*[1]/@k), $d/r/*[1]/b/.. is $d/r/*[1], root($d/r/*[1]/b) is $d,
              local-name($d/r/*[2]), count($d//b), exists($d/r/p:a), node-name($d/r/*[1]) eq QName("urn:p", "a"),
              count($l/r/z), string-join($l/r/z, ",")
            ) ! string(.), "|")
            """, ("d", foreign), ("l", local));
        text.Should().Be("2|p:a|urn:p|t|1|true|true|c|1|true|true|3|1,2,3");
    }

    [Fact]
    public async Task Foreign_document_serializes_with_its_own_namespaces()
    {
        var (store, _, foreign) = Stores();
        var text = await TextAsync(store, "declare variable $d external; serialize($d/r)", ("d", foreign));
        text.Should().Be(ForeignXml);
    }

    [Fact]
    public async Task Constructor_copies_foreign_content_into_its_own_store()
    {
        var (store, _, foreign) = Stores();
        var results = await RunAsync(store, "declare variable $d external; <w>{$d/r/*[1]}</w>", ("d", foreign));
        var w = results.Should().ContainSingle().Which.Should().BeOfType<XdmElement>().Subject;

        // Every node of the new tree is the query store's, and the copy kept its namespace.
        var all = new List<XdmNode>();
        var pending = new Stack<XdmNode>([w]);
        while (pending.Count > 0)
        {
            var n = pending.Pop();
            all.Add(n);
            if (n is XdmElement e)
                foreach (var id in e.Attributes.Concat(e.Children))
                    pending.Push(store.GetNode(id)!);
        }
        all.Should().HaveCount(5).And.OnlyContain(n => ReferenceEquals(n.Store, store));
        (await TextAsync(store, "declare variable $w external; namespace-uri($w/*) || '|' || serialize($w)", ("w", w)))
            .Should().Be("""urn:p|<w><p:a k="1" xmlns:p="urn:p"><b>t</b></p:a></w>""");
    }

    [Fact]
    public async Task Copy_modify_reads_a_foreign_source_in_its_own_store()
    {
        var (store, _, foreign) = Stores();
        var text = await TextAsync(store,
            "declare variable $d external; serialize(copy $c := $d/r modify delete node $c/c return $c)", ("d", foreign));
        text.Should().Be("""<r xmlns:p="urn:p"><p:a k="1"><b>t</b></p:a></r>""");
    }

    [Fact]
    public async Task In_scope_namespaces_of_a_foreign_element_read_its_own_store()
    {
        // The namespace axis (XSLT only) and these functions share the in-scope walk.
        var (store, _, foreign) = Stores();
        var text = await TextAsync(store, """
            declare variable $d external;
            let $e := $d/r/*[1]
            return string-join((
              string(namespace-uri-for-prefix('p', $e)),
              string-join(sort(in-scope-prefixes($e)), ',')
            ), "|")
            """, ("d", foreign));
        text.Should().Be("urn:p|p,xml");
    }

    [Fact]
    public async Task A_storeless_node_the_query_store_does_not_hold_is_refused()
    {
        // A node no store attached, with an id the query's store gives to another node: before
        // 3.3.0 its children were read from that other node, silently.
        var (store, _, _) = Stores();
        var orphan = new XdmElement
        {
            Id = new PhoenixmlDb.Core.NodeId(3), Document = PhoenixmlDb.Core.DocumentId.None,
            Namespace = PhoenixmlDb.Core.NamespaceId.None, LocalName = "orphan",
            Attributes = XdmElement.EmptyAttributes, Children = XdmElement.EmptyChildren,
            NamespaceDeclarations = XdmElement.EmptyNamespaceDeclarations,
        };
        var act = () => RunAsync(store, "declare variable $o external; count($o/*)", ("o", orphan));
        var error = (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which;
        error.ErrorCode.Should().Be("XPDY9002");
        error.ErrorNamespaceUri.Should().Be("https://schemas.phoenixml.dev/2026/functions");
    }

    [Fact]
    public async Task A_storeless_node_the_query_store_holds_is_accepted()
    {
        // A host whose store attaches no nodes (as a database provider may) keeps working: the
        // node is the one its provider returns for that id, even as a different object.
        var parsed = new XdmDocumentStore();
        var doc = parsed.LoadFromString("<r><x/><x/></r>");
        var copies = new Dictionary<PhoenixmlDb.Core.NodeId, XdmNode>();
        XdmNode Unattached(XdmNode n) => n switch
        {
            XdmDocument d => new XdmDocument { Id = d.Id, Document = d.Document, Children = d.Children, DocumentElement = d.DocumentElement },
            XdmElement e => new XdmElement { Id = e.Id, Document = e.Document, Namespace = e.Namespace, LocalName = e.LocalName,
                Prefix = e.Prefix, Attributes = e.Attributes, Children = e.Children, NamespaceDeclarations = e.NamespaceDeclarations, Parent = e.Parent },
            _ => n,
        };
        var provider = new DelegateNodeProvider(id => parsed.GetNode(id) is { } n ? copies.TryGetValue(id, out var c) ? c : copies[id] = Unattached(n) : null);
        var engine = new QueryEngine(nodeProvider: provider);
        var compilation = engine.Compile("declare variable $d external; count($d/r/x)");
        var context = engine.CreateContext();
        context.SetExternalVariable("d", Unattached(doc));   // a NEW object for the same node
        var results = new List<object?>();
        await foreach (var item in compilation.ExecutionPlan!.ExecuteAsync(context)) results.Add(item);
        results.Should().ContainSingle().Which.Should().Be(2L);
    }

    [Fact]
    public async Task Identity_and_order_hold_across_stores()
    {
        var (store, local, foreign) = Stores();
        var text = await TextAsync(store, """
            declare variable $d external;
            declare variable $l external;
            string-join((
              $d/r/*[1] << $d/r/*[2],
              $d/r/*[1] is $l/r/z[1],
              generate-id($d/r/*[1]) ne generate-id($l/r/z[1]),
              count(($d/r/*, $l/r/z) | ()) = 5
            ) ! string(.), "|")
            """, ("d", foreign), ("l", local));
        text.Should().Be("true|false|true|true");
    }
}
