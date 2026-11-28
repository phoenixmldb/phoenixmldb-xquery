using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// A host whose node provider is not an <see cref="INodeStore"/> (a <see cref="DelegateNodeProvider"/>)
/// gets the same results as a host whose provider is the store, for nodes of a store that the
/// document resolver loaded: the namespace of a node comes from the node's own store
/// (<see cref="Xdm.Nodes.XdmNode.Store"/>), not only from the context's node provider.
/// </summary>
public sealed class NamespaceFromNodeStoreTests
{
    private const string Output = "http://www.w3.org/2010/xslt-xquery-serialization";

    private static XdmDocumentStore Documents()
    {
        var docs = new XdmDocumentStore();
        docs.LoadFromString($"<output:serialization-parameters xmlns:output='{Output}'><output:method value='text'/></output:serialization-parameters>", "urn:p");
        docs.LoadFromString("<r xmlns:q='urn:q'><q:a q:b='1'>t</q:a></r>", "urn:d");
        docs.LoadFromString("<x xml:lang='en'/>", "urn:l");
        docs.LoadFromString("<map xmlns='http://www.w3.org/2005/xpath-functions'><string key='a'>1</string></map>", "urn:j");
        docs.LoadFromString($"<output:serialization-parameters xmlns:output='{Output}'><output:omit-xml-declaration value='yes'/>"
            + "<output:use-character-maps><output:character-map character='t' map-string='T'/></output:use-character-maps>"
            + "</output:serialization-parameters>", "urn:p2");
        return docs;
    }

    private static async Task<string> RunAsync(QueryEngine engine, string query)
    {
        try
        {
            var compiled = engine.Compile(query);
            if (!compiled.Success)
                return "STATIC " + string.Join("; ", compiled.Errors.Select(e => e.Message));
            var items = new List<object?>();
            await foreach (var item in engine.ExecuteAsync(compiled.ExecutionPlan!))
                items.Add(item);
            return string.Join(" ", items.Select(i => i?.ToString()));
        }
        catch (XQueryRuntimeException e)
        {
            return "ERR " + e.ErrorCode;
        }
        catch (PhoenixmlDb.XQuery.Functions.XQueryException e)
        {
            return "ERR " + e.ErrorCode;
        }
    }

    /// <summary>A document resolver that is not an <see cref="INodeStore"/>: no namespace resolver can come from it.</summary>
    private sealed class NotAStoreResolver(XdmDocumentStore docs) : IDocumentResolver
    {
        public Xdm.Nodes.XdmDocument? ResolveDocument(string uri) => docs.ResolveDocument(uri);
        public bool IsDocumentAvailable(string uri) => docs.IsDocumentAvailable(uri);
        public IEnumerable<Xdm.Nodes.XdmNode> ResolveCollection(string? uri) => docs.ResolveCollection(uri);
    }

    public static TheoryData<string> Queries => new()
    {
        "serialize(doc('urn:d')/*, doc('urn:p2')/*)",
        "namespace-uri-from-QName(node-name(doc('urn:d')//*:a))",
        "string-join(in-scope-prefixes(doc('urn:d')//*:a) ! (. || '=' || namespace-uri-for-prefix(., doc('urn:d')//*:a)), ',')",
        "count((validate lax { doc('urn:d') })//*:a)",
        "string-join((validate lax { doc('urn:d') })//*:a/@* ! namespace-uri(.), ',')",
        "serialize('x', doc('urn:p')/*)",
        "serialize(doc('urn:d')/*)",
        "serialize(doc('urn:d')/*, map { 'method': 'xml' })",
        "serialize(doc('urn:d')/*, map { 'omit-xml-declaration': false() })",
        "serialize(doc('urn:d')/*, map { 'indent': true() })",
        "namespace-uri(doc('urn:d')//*:a)",
        "string-join(doc('urn:d')//*:a/@* ! namespace-uri(.), ',')",
        "name(doc('urn:d')//*:a)",
        "path(doc('urn:d')//*:a)",
        "lang('en', doc('urn:l')/x)",
        "xml-to-json(doc('urn:j')/*)",
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task A_provider_that_is_not_a_store_gives_the_store_s_result(string query)
    {
        var docs = Documents();
        var asStore = new QueryEngine(nodeProvider: docs, documentResolver: docs);
        var notAStore = new QueryEngine(nodeProvider: new DelegateNodeProvider(docs.GetNode), documentResolver: docs);

        var expected = await RunAsync(asStore, query);
        expected.Should().NotStartWith("ERR").And.NotStartWith("STATIC");
        (await RunAsync(notAStore, query)).Should().Be(expected);
    }

    /// <summary>
    /// A node of another store: the node provider is a store of its own, whose namespace ids name
    /// other URIs, and the documents come from the document resolver's store. A namespace read in
    /// the provider instead of the node's own store gives a wrong URI.
    /// </summary>
    [Theory]
    [MemberData(nameof(Queries))]
    public async Task A_node_of_another_store_is_read_in_its_own_store(string query)
    {
        var docs = Documents();
        var asStore = new QueryEngine(nodeProvider: docs, documentResolver: docs);
        var other = new XdmDocumentStore();
        other.LoadFromString("<z xmlns:w='urn:wrong' xmlns:v='urn:wrong2' xmlns:u='urn:wrong3'><w:y w:b='1' v:c='2'/></z>", "urn:other");
        var otherProvider = new QueryEngine(nodeProvider: other, documentResolver: docs);

        var expected = await RunAsync(asStore, query);
        (await RunAsync(otherProvider, query)).Should().Be(expected);
    }

    /// <summary>
    /// The same with a document resolver that is not a store either: the context has no namespace
    /// resolver, so only the node's own store can give the namespaces.
    /// </summary>
    [Theory]
    [MemberData(nameof(Queries))]
    public async Task With_no_store_but_the_node_s_own_the_result_is_the_store_s(string query)
    {
        var docs = Documents();
        var asStore = new QueryEngine(nodeProvider: docs, documentResolver: docs);
        var neither = new QueryEngine(nodeProvider: new DelegateNodeProvider(docs.GetNode), documentResolver: new NotAStoreResolver(docs));

        var expected = await RunAsync(asStore, query);
        (await RunAsync(neither, query)).Should().Be(expected);
    }

    /// <summary>
    /// The context's namespace resolver, the last fallback of a node no store holds, comes from the
    /// document resolver when it is a store and the node provider is not: it was null.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_context_namespace_resolver_comes_from_a_document_store(bool behindPolicy)
    {
        var docs = Documents();
        // A host may give a resolver that enforces a policy around the store.
        var resolver = behindPolicy
            ? new PhoenixmlDb.XQuery.Security.PolicyEnforcingResolver(docs, PhoenixmlDb.XQuery.Security.ResourcePolicy.CreateBuilder().AllowScheme("*").Build())
            : (IDocumentResolver)docs;
        var engine = new QueryEngine(nodeProvider: new DelegateNodeProvider(docs.GetNode), documentResolver: resolver);
        ((Xdm.Nodes.IXdmNodeStore)docs).TryGetNamespaceId("urn:q", out var id).Should().BeTrue();

        using var context = engine.CreateContext();

        context.NamespaceResolver.Should().NotBeNull();
        context.NamespaceResolver!(id).Should().Be("urn:q");
    }

    /// <summary>A store that holds a node and knows none of its ids; not an <see cref="INodeStore"/>.</summary>
    private sealed class StoreThatKnowsNoIds : Xdm.Nodes.IXdmNodeStore
    {
        public Xdm.Nodes.XdmNode? GetNode(Core.NodeId id) => null;
        public string? GetNamespaceUri(Core.NamespaceId id) => null;
        public bool TryGetNamespaceId(string uri, out Core.NamespaceId id)
        {
            id = Core.NamespaceId.None;
            return false;
        }
    }

    private static Xdm.Nodes.XdmElement Element(Core.NamespaceId ns) => new()
    {
        Id = new Core.NodeId(1),
        Document = Core.DocumentId.None,
        Namespace = ns,
        LocalName = "a",
        Attributes = Xdm.Nodes.XdmElement.EmptyAttributes,
        Children = Xdm.Nodes.XdmElement.EmptyChildren,
        NamespaceDeclarations = Xdm.Nodes.XdmElement.EmptyNamespaceDeclarations,
    };

    /// <summary>
    /// A node that a store holds gets its namespace URIs from that store only. An id that store
    /// does not know has no URI: the context's stores are other stores, and there the same id
    /// names another URI. Only a node that no store holds is read in the context's stores.
    /// </summary>
    [Fact]
    public void A_node_that_a_store_holds_gets_no_uri_from_another_store()
    {
        var other = new XdmDocumentStore();
        other.LoadFromString("<w:y xmlns:w='urn:wrong'/>", "urn:other");
        ((Xdm.Nodes.IXdmNodeStore)other).TryGetNamespaceId("urn:wrong", out var id).Should().BeTrue();
        var held = Element(id);
        held.AttachStore(new StoreThatKnowsNoIds());
        var free = Element(id);
        var engine = new QueryEngine(nodeProvider: other, documentResolver: other);
        using var context = engine.CreateContext();

        NodeNamespaces.UriOf(held, id, context).Should().BeNull();
        NodeNamespaces.UriOf(held, id, (INodeProvider)other).Should().BeNull();
        PhoenixmlDb.XQuery.Functions.NamespaceUriFunction.ResolveNsId(held, id, context).Should().BeEmpty();

        NodeNamespaces.UriOf(free, id, context).Should().Be("urn:wrong");
        NodeNamespaces.UriOf(free, id, (INodeProvider)other).Should().Be("urn:wrong");
    }

    /// <summary>
    /// The same rule for the nodes of the ids of a node, and for the store to read its tree in: a
    /// node that a store holds is read in that store only, also when that store is not an
    /// <see cref="INodeStore"/>. Only a node that no store holds is read in the context's store.
    /// </summary>
    [Fact]
    public void A_node_that_a_store_holds_gets_no_node_from_another_store()
    {
        var other = new XdmDocumentStore();
        var document = other.LoadFromString("<w:y xmlns:w='urn:wrong'/>", "urn:other");
        var childId = document.Children[0];
        ((Xdm.Nodes.IXdmNodeStore)other).TryGetNamespaceId("urn:wrong", out var ns).Should().BeTrue();
        var held = Element(Core.NamespaceId.None);
        held.AttachStore(new StoreThatKnowsNoIds());
        var free = Element(Core.NamespaceId.None);

        NodeNamespaces.NodeOf(held, childId, other).Should().BeNull();
        var heldStore = NodeNamespaces.StoreOf(held, other);
        heldStore.Should().NotBeNull().And.NotBeSameAs(other);
        heldStore!.GetNode(childId).Should().BeNull();
        heldStore.GetNamespaceUri(ns).Should().BeNull();

        NodeNamespaces.NodeOf(free, childId, other).Should().BeSameAs(other.GetNode(childId));
        NodeNamespaces.StoreOf(free, other).Should().BeSameAs(other);
    }
}
