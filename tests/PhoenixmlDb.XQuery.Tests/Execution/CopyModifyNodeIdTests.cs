using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// The node ids of copy modify expressions. Each one's copies were made in a store that counted its
/// ids from 1,000,000,000, and were then registered in the query's store: two copy modify expressions
/// of one query made nodes with the same ids, and the second one's replaced the first one's there.
/// <c>(copy $c := $d/r/a modify () return $c, copy $c := $d/r/b modify () return $c)</c> gave
/// <c>&lt;a&gt;&lt;b1/&gt;&lt;/a&gt;&lt;b&gt;&lt;b1/&gt;&lt;/b&gt;</c> (3.2.0 too), with no error. Now the copies take the ids of
/// the query's store. Each test also checks that every node of the result is the node its store
/// holds under its id, with the right parent.
/// </summary>
public class CopyModifyNodeIdTests
{
    private const string Doc = "urn:test:a";
    private const string Xml = "<r><a><a1/></a><b><b1/></b></r>";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup(int largeDocument = 0)
    {
        var store = new XdmDocumentStore();
        // A store that has given many ids (a long-lived store of a host): the large document's ids
        // start just below 1,000,000,000, where each copy modify store started counting.
        if (largeDocument > 0)
            ((INodeBuilder)store).ReserveIdsThrough(new NodeId(999_999_990));
        if (largeDocument > 0)
            store.LoadFromString("<big>" + string.Concat(Enumerable.Range(0, largeDocument).Select(i => $"<e n='{i}'><f/></e>")) + "</big>", "urn:test:big");
        store.LoadFromString(Xml, Doc);
        return (store, new QueryEngine(nodeProvider: store, documentResolver: store));
    }

    private static ExecutionPlan Compile(QueryEngine engine, string query)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        return compiled.ExecutionPlan!;
    }

    private static async Task<List<object?>> ItemsAsync(QueryEngine engine, string query)
    {
        var items = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(Compile(engine, query)))
            items.Add(item);
        return items;
    }

    private static async Task<string> RunAsync(QueryEngine engine, string query)
        => string.Join(" ", (await ItemsAsync(engine, query)).Select(i => i?.ToString()));

    // Every node of the tree is the one its store holds under its id, and its parent is the node it
    // is a child of: a node whose id another node took is not.
    private static void RequireOwnIds(XdmDocumentStore store, XdmNode root)
    {
        var seen = new HashSet<NodeId>();
        void Walk(XdmNode node)
        {
            seen.Add(node.Id).Should().BeTrue($"the id {node.Id} is in the tree once");
            var held = store.GetNode(node.Id);
            held.Should().NotBeNull($"the store holds {node.Id}");
            held!.NodeKind.Should().Be(node.NodeKind);
            IReadOnlyList<NodeId> children = node switch { XdmElement e => e.Children, XdmDocument d => d.Children, _ => [] };
            foreach (var childId in children)
            {
                var child = store.GetNode(childId);
                child.Should().NotBeNull($"the store holds the child {childId}");
                child!.Parent.Should().Be(node.Id, $"the child {childId} of {node.Id} has it as its parent");
                Walk(child);
            }
        }
        Walk(root);
    }

    [Fact]
    public async Task Two_copies_in_one_sequence_keep_their_own_children()
    {
        var (store, engine) = Setup();

        var items = await ItemsAsync(engine, $"(copy $c := doc('{Doc}')/r/a modify () return $c, copy $c := doc('{Doc}')/r/b modify () return $c)");

        items.Should().HaveCount(2);
        (await RunAsync(engine, $"serialize((copy $c := doc('{Doc}')/r/a modify () return $c, copy $c := doc('{Doc}')/r/b modify () return $c))"))
            .Should().Be("<a><a1/></a><b><b1/></b>");
        foreach (var node in items.Cast<XdmNode>())
            RequireOwnIds(store, node);
        (await RunAsync(engine, $"serialize(doc('{Doc}'))")).Should().Be(Xml);
    }

    // The report: two copies inserted into a third.
    [Fact]
    public async Task Two_copies_inserted_into_a_copy_are_copies_of_their_sources()
    {
        var (_, engine) = Setup();

        (await RunAsync(engine,
                $"serialize(copy $d := doc('{Doc}') modify insert node (copy $c := $d/r/a modify () return $c, copy $c := $d/r/b modify () return $c) into $d/r return $d/r)"))
            .Should().Be("<r><a><a1/></a><b><b1/></b><a><a1/></a><b><b1/></b></r>");
    }

    [Fact]
    public async Task A_copy_in_each_iteration_of_a_loop_keeps_its_own_children()
    {
        var (store, engine) = Setup();

        var items = await ItemsAsync(engine,
            $"for $i in 1 to 4 return copy $c := doc('{Doc}')/r modify insert node <n>{{$i}}</n> into $c return $c");

        items.Should().HaveCount(4);
        for (var i = 0; i < 4; i++)
        {
            var copy = (XdmElement)items[i]!;
            RequireOwnIds(store, copy);
            var names = copy.Children.Select(id => store.GetNode(id)).OfType<XdmElement>().Select(e => e.LocalName);
            names.Should().Equal("a", "b", "n");
            store.GetNode(copy.Children[^1])!.StringValue.Should().Be((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public async Task A_copy_inside_the_modify_clause_of_another_keeps_its_own_children()
    {
        var (store, engine) = Setup();

        var items = await ItemsAsync(engine,
            $"copy $o := doc('{Doc}')/r modify insert node (copy $i := $o/a modify insert node <x/> into $i return $i) into $o/b return $o");

        var copy = (XdmElement)items.Single()!;
        RequireOwnIds(store, copy);
        (await RunAsync(engine,
                $"serialize(copy $o := doc('{Doc}')/r modify insert node (copy $i := $o/a modify insert node <x/> into $i return $i) into $o/b return $o)"))
            .Should().Be("<r><a><a1/></a><b><b1/><a><a1/><x/></a></b></r>");
    }

    [Fact]
    public async Task A_copy_and_a_constructed_element_in_one_sequence_keep_their_children()
    {
        var (store, engine) = Setup();
        const string query = "(copy $c := doc('" + Doc + "')/r/a modify () return $c, <k><m/></k>, copy $c := doc('" + Doc + "')/r/b modify () return $c)";

        foreach (var node in (await ItemsAsync(engine, query)).Cast<XdmNode>())
            RequireOwnIds(store, node);
        (await RunAsync(engine, $"serialize({query})")).Should().Be("<a><a1/></a><k><m/></k><b><b1/></b>");
    }

    // The engine's path: the list of a run is applied by the host, and the copies are its content.
    [Fact]
    public async Task Two_copies_inserted_into_a_stored_document_on_the_deferred_path()
    {
        var (store, engine) = Setup();
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;
        await foreach (var _ in Compile(engine,
                $"insert node (copy $c := doc('{Doc}')/r/a modify () return $c, copy $c := doc('{Doc}')/r/b modify () return $c) into doc('{Doc}')/r")
            .ExecuteAsync(context)) { }

        PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);

        (await RunAsync(engine, $"serialize(doc('{Doc}'))")).Should().Be("<r><a><a1/></a><b><b1/></b><a><a1/></a><b><b1/></b></r>");
        RequireOwnIds(store, store.ResolveDocument(Doc)!);
    }

    // The ids are unique against everything the store holds: a large document loaded first, whose
    // node ids reach past 1,000,000,000 (where a copy modify store started counting), and the copies.
    [Fact]
    public async Task Copies_take_ids_the_store_has_not_given_a_loaded_document()
    {
        var (store, engine) = Setup(largeDocument: 3000);
        var bigBefore = await RunAsync(engine, "serialize(doc('urn:test:big'))");

        var items = await ItemsAsync(engine, $"(copy $c := doc('{Doc}')/r/a modify () return $c, copy $c := doc('{Doc}')/r/b modify () return $c)");

        foreach (var node in items.Cast<XdmNode>())
            RequireOwnIds(store, node);
        RequireOwnIds(store, store.ResolveDocument("urn:test:big")!);
        (await RunAsync(engine, "serialize(doc('urn:test:big'))")).Should().Be(bigBefore);
        (await RunAsync(engine, "count(doc('urn:test:big')//f)")).Should().Be("3000");
    }
}
