using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A query's pending updates are applied when it ends, at every entry point, to the store of each
/// target node (XQUF 3.0 §2.5; BUGS.md #127). They were dropped by every caller of
/// <see cref="ExecutionPlan.ExecuteAsync"/>, and refused with XUST0002 by <see cref="QueryEngine"/>.
/// A target in a store that cannot be updated is phx:XUDY9001, and then nothing changes.
/// </summary>
public class UpdateApplicationTests
{
    private const string Doc = "urn:test:d";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup(string xml)
    {
        var store = new XdmDocumentStore();
        store.LoadFromString(xml, Doc);
        return (store, new QueryEngine(nodeProvider: store, documentResolver: store));
    }

    private static async Task<string> RunAsync(QueryEngine engine, string query, object? contextItem = null)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var items = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(compiled.ExecutionPlan!, initialContextItem: contextItem))
            items.Add(item);
        return string.Join(" ", items.Select(i => i?.ToString()));
    }

    public static TheoryData<string, string, string> Updates => new()
    {
        { "insert node <x/> into doc('urn:test:d')/r", "a b x", "ab" },
        { "insert node <x/> as first into doc('urn:test:d')/r", "x a b", "ab" },
        { "insert node <x>X</x> after doc('urn:test:d')/r/a", "a x b", "aXb" },
        { "delete node doc('urn:test:d')/r/a", "b", "b" },
        { "replace node doc('urn:test:d')/r/a with <y>Y</y>", "y b", "Yb" },
        { "replace value of node doc('urn:test:d')/r/a with 'new'", "a b", "newb" },
        { "rename node doc('urn:test:d')/r/a as 'z'", "z b", "ab" },
    };

    [Theory]
    [MemberData(nameof(Updates))]
    public async Task ATopLevelUpdateChangesTheDocument(string update, string names, string text)
    {
        var (_, engine) = Setup("<r><a>a</a><b>b</b></r>");

        (await RunAsync(engine, update)).Should().BeEmpty();

        (await RunAsync(engine, "string-join(doc('urn:test:d')/r/*!name(), ' ')")).Should().Be(names);
        (await RunAsync(engine, "string(doc('urn:test:d')/r)")).Should().Be(text, "the string value follows the change");
        (await RunAsync(engine, "string(doc('urn:test:d'))")).Should().Be(text);
    }

    [Fact]
    public async Task AnInsertedNodeIsACopyAndTheSourceStaysWhereItWas()
    {
        // The source node itself was placed: inserted into its own descendant, the tree became a
        // cycle (a query that never ended); inserted elsewhere, it left its old place.
        var (_, engine) = Setup("<r><a/><b>B</b></r>");

        await RunAsync(engine, "insert node doc('urn:test:d')/r into doc('urn:test:d')/r/a");
        await RunAsync(engine, "insert node doc('urn:test:d')/r/b into doc('urn:test:d')/r/a");

        (await RunAsync(engine, "string-join(doc('urn:test:d')/r/*!name(), ' ')")).Should().Be("a b");
        (await RunAsync(engine, "string-join(doc('urn:test:d')/r/a/*!name(), ' ')")).Should().Be("r b");
        (await RunAsync(engine, "count(doc('urn:test:d')/r/a/r/a/*)")).Should().Be("0", "the copy is of r as it was");
        (await RunAsync(engine, "string(doc('urn:test:d')/r/a/b is doc('urn:test:d')/r/b)")).Should().Be("false");
        (await RunAsync(engine, "string(doc('urn:test:d')/r)")).Should().Be("BBB");
    }

    [Fact]
    public async Task AnInsertedDocumentNodeIsItsChildren()
    {
        // A document node stands for its children; placed itself, it put the document inside one
        // of its own elements (a cycle).
        var (_, engine) = Setup("<r><a/></r>");

        await RunAsync(engine, "insert node doc('urn:test:d') into doc('urn:test:d')/r/a");

        (await RunAsync(engine, "string-join(doc('urn:test:d')/r/a/*!name(), ' ')")).Should().Be("r");
        (await RunAsync(engine, "count(doc('urn:test:d')/r/a/r/a/*)")).Should().Be("0");
    }

    [Fact]
    public async Task AttributesAreInsertedReplacedAndChanged()
    {
        var (_, engine) = Setup("""<r k="1"><a/></r>""");

        await RunAsync(engine, "insert node attribute n {'2'} into doc('urn:test:d')/r");
        await RunAsync(engine, "replace value of node doc('urn:test:d')/r/@k with 'one'");
        await RunAsync(engine, "rename node doc('urn:test:d')/r/@n as 'm'");

        (await RunAsync(engine, "string-join(doc('urn:test:d')/r/@*!(name() || '=' || .), ' ')")).Should().Be("k=one m=2");
        (await RunAsync(engine, "count(doc('urn:test:d')/r/node())")).Should().Be("1", "an attribute is not a child");
    }

    [Fact]
    public async Task UpdatesThroughTheExecutionPlanAreApplied()
    {
        // The facade and the command line run the plan themselves; the list was dropped there.
        var (_, engine) = Setup("<r><a/></r>");
        var compiled = engine.Compile("insert node <x/> into doc('urn:test:d')/r");
        var context = engine.CreateContext();

        await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(context))
        {
        }

        (await RunAsync(engine, "count(doc('urn:test:d')/r/x)")).Should().Be("1");
        context.PendingUpdates.HasUpdates.Should().BeFalse("the list is applied once");
    }

    [Fact]
    public async Task AListAHostAppliesItselfUpdatesStringValues()
    {
        // PendingUpdateApplicator.Apply is public; string values are computed again at its end,
        // not only when a query ends.
        var (store, engine) = Setup("<r><a>a</a><b>b</b></r>");
        var a = store.ResolveDocument(Doc)!;
        var r = (XdmElement)store.GetNode(a.DocumentElement!.Value)!;
        var pul = new PhoenixmlDb.XQuery.Ast.PendingUpdateList();
        pul.AddDelete(store.GetNode(r.Children[0])!);

        PendingUpdateApplicator.Apply(pul, store);

        (await RunAsync(engine, "string(doc('urn:test:d')/r)")).Should().Be("b");
        (await RunAsync(engine, "string(doc('urn:test:d'))")).Should().Be("b");
    }

    [Fact]
    public async Task ATargetInAStoreThatCannotBeUpdatedIsXUDY9001AndNothingChanges()
    {
        var (store, _) = Setup("<r><a/></r>");
        var readOnly = new ReadOnlyStore();
        var engine = new QueryEngine(nodeProvider: readOnly, documentResolver: store);

        var act = () => RunAsync(engine, "(delete node doc('urn:test:d')/r/a, delete node ./r)", readOnly.Document);

        var error = (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which;
        error.ErrorCode.Should().Be("XUDY9001");
        error.ErrorNamespaceUri.Should().Be("https://schemas.phoenixml.dev/2026/functions");
        readOnly.Document.Children.Should().ContainSingle("the read-only document is not changed");
        (await RunAsync(new QueryEngine(nodeProvider: store, documentResolver: store), "count(doc('urn:test:d')/r/a)"))
            .Should().Be("1", "nothing is changed when one target cannot be updated, also in a store that can");
    }

    [Fact]
    public async Task CopyModifyOfANodeOfAStoreThatCannotBeUpdatedWorks()
    {
        var readOnly = new ReadOnlyStore();
        var engine = new QueryEngine(nodeProvider: readOnly);

        // The double constructs no nodes (db-engine's provider does), so the change constructs none.
        var result = await RunAsync(engine,
            "copy $c := . modify delete node $c/r return count($c/r) || ' ' || count(./r)", readOnly.Document);

        result.Should().Be("0 1", "the copy changes, the stored document does not");
        readOnly.Document.Children.Should().ContainSingle();
    }

    /// <summary>A store that cannot be updated, as a database's stored documents: a document with one element r.</summary>
    private sealed class ReadOnlyStore : INodeProvider, IXdmNodeStore
    {
        private readonly Dictionary<NodeId, XdmNode> _nodes = [];

        public ReadOnlyStore()
        {
            var doc = new DocumentId(77);
            var root = new XdmElement
            {
                Id = new NodeId(7_000_002),
                Document = doc,
                Namespace = NamespaceId.None,
                LocalName = "r",
                Attributes = XdmElement.EmptyAttributes,
                Children = XdmElement.EmptyChildren,
                NamespaceDeclarations = [],
                StringValueResolver = _ => "",
            };
            Document = new XdmDocument
            {
                Id = new NodeId(7_000_001),
                Document = doc,
                Children = [root.Id],
                DocumentElement = root.Id,
                DocumentElementLocalName = "r",
                StringValueResolver = _ => "",
            };
            root.Parent = Document.Id;
            foreach (var node in new XdmNode[] { Document, root })
            {
                node.AttachStore(this);
                _nodes[node.Id] = node;
            }
        }

        public XdmDocument Document { get; }

        public XdmNode? GetNode(NodeId nodeId) => _nodes.GetValueOrDefault(nodeId);

        public string? GetNamespaceUri(NamespaceId id) => id == NamespaceId.None ? "" : null;

        public bool TryGetNamespaceId(string uri, out NamespaceId id)
        {
            id = NamespaceId.None;
            return uri.Length == 0;
        }
    }
}
