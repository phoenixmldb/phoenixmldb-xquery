using FluentAssertions;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Regression tests for issue #188 — cross-store document-order identity.
///
/// Document order and node identity ride on the store-global tree ordinal
/// (<c>(TreeOrdinal, NodeId)</c>). Nodes parsed by two independent
/// <see cref="XdmDocumentStore"/> instances share the same NodeId range, so before the fix
/// they interleaved by NodeId, dedup dropped distinct nodes, and <c>is</c> conflated them.
/// These tests assert the cross-store behavior is now correct WHILE the single-store cases
/// (constructed-last, multi-doc load order) are unchanged.
/// </summary>
public sealed class DocumentOrderBug188Tests
{
    private static async Task<List<XdmNode>> SelectNodesAsync(XdmDocumentStore store, string query, XdmNode contextItem)
    {
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compilation = engine.Compile(query);
        compilation.Success.Should().BeTrue(string.Join("; ", compilation.Errors.Select(e => e.Message)));
        var context = engine.CreateContext(initialContextItem: contextItem);
        var nodes = new List<XdmNode>();
        await foreach (var item in compilation.ExecutionPlan!.ExecuteAsync(context))
        {
            if (item is XdmNode n) nodes.Add(n);
        }
        return nodes;
    }

    private static async Task<List<object?>> RunAsync(
        XdmDocumentStore runner, string query, params (string name, object? value)[] bindings)
    {
        var engine = new QueryEngine(nodeProvider: runner, documentResolver: runner);
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

    // ---- Bug: cross-store union groups by tree, does not interleave / drop by NodeId ----

    [Fact]
    public async Task CrossStore_union_groups_by_tree_and_keeps_all_nodes()
    {
        var storeA = new XdmDocumentStore();
        var docA = storeA.LoadFromString("<r><x>a1</x><x>a2</x></r>", "a");
        var storeB = new XdmDocumentStore();
        var docB = storeB.LoadFromString("<r><x>b1</x><x>b2</x></r>", "b");

        var aNodes = await SelectNodesAsync(storeA, "//x", docA);
        var bNodes = await SelectNodesAsync(storeB, "//x", docB);

        aNodes.Should().HaveCount(2);
        bNodes.Should().HaveCount(2);

        // Precondition of the bug: the two stores reuse the same NodeId range.
        aNodes[0].Id.Should().Be(bNodes[0].Id);
        aNodes[1].Id.Should().Be(bNodes[1].Id);
        // But the fix gives them distinct tree ordinals.
        aNodes[0].TreeOrdinal.Should().NotBe(bNodes[0].TreeOrdinal);

        object?[] a = aNodes.Cast<object?>().ToArray();
        object?[] b = bNodes.Cast<object?>().ToArray();

        var results = await RunAsync(storeA,
            "declare variable $a external; declare variable $b external; $a | $b",
            ("a", a), ("b", b));

        // All four distinct nodes are kept (no node-loss dedup) ...
        results.Should().HaveCount(4);
        var values = results.Cast<XdmNode>().Select(n => n.StringValue).ToList();
        // ... and grouped by tree (store A loaded first → lower ordinal → first), not interleaved.
        values.Should().Equal("a1", "a2", "b1", "b2");
    }

    [Fact]
    public async Task CrossStore_is_distinguishes_distinct_nodes_sharing_nodeid()
    {
        var storeA = new XdmDocumentStore();
        var docA = storeA.LoadFromString("<r><x>a</x></r>", "a");
        var storeB = new XdmDocumentStore();
        var docB = storeB.LoadFromString("<r><x>b</x></r>", "b");

        var xa = (await SelectNodesAsync(storeA, "//x", docA)).Single();
        var xb = (await SelectNodesAsync(storeB, "//x", docB)).Single();
        xa.Id.Should().Be(xb.Id); // collide on NodeId

        var distinct = await RunAsync(storeA,
            "declare variable $p external; declare variable $q external; $p is $q",
            ("p", xa), ("q", xb));
        distinct.Single().Should().Be(false);

        var same = await RunAsync(storeA,
            "declare variable $p external; $p is $p", ("p", xa));
        same.Single().Should().Be(true);
    }

    [Fact]
    public async Task CrossStore_dedup_keeps_both_nodes_sharing_nodeid()
    {
        var storeA = new XdmDocumentStore();
        var docA = storeA.LoadFromString("<r><x>a</x></r>", "a");
        var storeB = new XdmDocumentStore();
        var docB = storeB.LoadFromString("<r><x>b</x></r>", "b");

        var xa = (await SelectNodesAsync(storeA, "//x", docA)).Single();
        var xb = (await SelectNodesAsync(storeB, "//x", docB)).Single();

        var results = await RunAsync(storeA,
            "declare variable $p external; declare variable $q external; $p | $q",
            ("p", xa), ("q", xb));

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task CrossStore_node_before_orders_by_tree()
    {
        var storeA = new XdmDocumentStore();
        var docA = storeA.LoadFromString("<r><x>a</x></r>", "a");
        var storeB = new XdmDocumentStore();
        var docB = storeB.LoadFromString("<r><x>b</x></r>", "b");

        var xa = (await SelectNodesAsync(storeA, "//x", docA)).Single();
        var xb = (await SelectNodesAsync(storeB, "//x", docB)).Single();

        // Store A parsed first → lower tree ordinal → A precedes B.
        var before = await RunAsync(storeA,
            "declare variable $p external; declare variable $q external; $p << $q",
            ("p", xa), ("q", xb));
        before.Single().Should().Be(true);

        var after = await RunAsync(storeA,
            "declare variable $p external; declare variable $q external; $p >> $q",
            ("p", xa), ("q", xb));
        after.Single().Should().Be(false);
    }

    // ---- No regression: single-store ordering is unchanged ----

    [Fact]
    public async Task SingleStore_constructed_node_still_sorts_last()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r><x>x</x></r>", "a");

        var results = await RunAsync(store, "doc('a')//x | <c/>");

        results.Should().HaveCount(2);
        var kinds = results.Cast<XdmNode>().ToList();
        kinds[0].StringValue.Should().Be("x");           // parsed x first
        kinds[1].NodeName!.Value.LocalName.Should().Be("c"); // constructed c last
    }

    [Fact]
    public async Task SingleStore_multidoc_union_preserves_load_order()
    {
        // One store, doc 'a' loaded before doc 'b'. Even querying b first, a's nodes precede b's.
        var store = new XdmDocumentStore();
        store.LoadFromString("<a><x1/><x2/><x3/></a>", "a");
        store.LoadFromString("<b><y1/><y2/></b>", "b");

        var results = await RunAsync(store, "doc('b')//* | doc('a')//*");

        var names = results.Cast<XdmNode>().Select(n => n.NodeName!.Value.LocalName).ToList();
        // //* selects each root element and its descendants; a's whole tree precedes b's.
        names.Should().Equal("a", "x1", "x2", "x3", "b", "y1", "y2");
    }
}
