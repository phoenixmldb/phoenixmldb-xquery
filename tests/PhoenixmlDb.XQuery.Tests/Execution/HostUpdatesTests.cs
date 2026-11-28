using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// What a host needs to apply a query's updates itself (the database engine): a run that leaves
/// its updates in the list (<see cref="QueryExecutionContext.DeferPendingUpdates"/>), the public
/// <see cref="PendingUpdateApplicator.ApplyUpdates"/>, whether a compiled query is updating
/// (<see cref="ExecutionPlan.IsUpdating"/>), the size of a store
/// (<see cref="XdmDocumentStore.NodeCount"/>), and a node of another store inserted into a store.
/// </summary>
public class HostUpdatesTests
{
    private const string Doc = "urn:test:d";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup(TimeSpan? zone = null, string xml = "<r><a>a</a><b>b</b></r>")
    {
        var store = new XdmDocumentStore();
        store.LoadFromString(xml, Doc);
        return (store, new QueryEngine(nodeProvider: store, documentResolver: store) { ImplicitTimezone = zone });
    }

    private static ExecutionPlan Compile(QueryEngine engine, string query)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        return compiled.ExecutionPlan!;
    }

    private static async Task<List<object?>> ItemsAsync(ExecutionPlan plan, QueryExecutionContext context)
    {
        var items = new List<object?>();
        await foreach (var item in plan.ExecuteAsync(context))
            items.Add(item);
        return items;
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

    private static Task<string> Names(QueryEngine engine)
        => RunAsync(engine, $"string-join(doc('{Doc}')/r/*!name(), ' ')");

    private static Task<string> Serialized(QueryEngine engine)
        => RunAsync(engine, $"serialize(doc('{Doc}'))");

    // The two places where a run applies its updates: with an implicit timezone and without one.
    public static TheoryData<bool> BothApplySites => new() { false, true };

    // ---- DeferPendingUpdates ----

    [Theory]
    [MemberData(nameof(BothApplySites))]
    public async Task With_DeferPendingUpdates_a_run_leaves_its_updates_in_the_list(bool timezone)
    {
        var (_, engine) = Setup(timezone ? TimeSpan.FromHours(2) : null);
        var plan = Compile(engine, $"(delete node doc('{Doc}')/r/a, 'x')");
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;

        (await ItemsAsync(plan, context)).Should().Equal("x");

        context.PendingUpdates.Primitives.Should().ContainSingle().Which.Should().BeOfType<DeletePrimitive>();
        (await Names(engine)).Should().Be("a b");

        PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);

        (await Names(engine)).Should().Be("b");
        context.PendingUpdates.HasUpdates.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothApplySites))]
    public async Task Without_it_a_run_applies_its_updates_when_its_items_end(bool timezone)
    {
        var (_, engine) = Setup(timezone ? TimeSpan.FromHours(2) : null);
        var plan = Compile(engine, $"(delete node doc('{Doc}')/r/a, 'x')");
        using var context = engine.CreateContext();

        (await ItemsAsync(plan, context)).Should().Equal("x");

        (await Names(engine)).Should().Be("b");
        context.PendingUpdates.HasUpdates.Should().BeFalse();
    }

    [Fact]
    public async Task The_lists_of_several_runs_are_merged_and_applied_once()
    {
        var (_, engine) = Setup();
        var merged = new PendingUpdateList();
        QueryExecutionContext? last = null;
        foreach (var query in new[] { $"delete node doc('{Doc}')/r/a", $"rename node doc('{Doc}')/r/b as 'c'" })
        {
            var context = engine.CreateContext();
            context.DeferPendingUpdates = true;
            await ItemsAsync(Compile(engine, query), context);
            merged.Merge(context.PendingUpdates);
            last?.Dispose();
            last = context;
        }

        (await Names(engine)).Should().Be("a b");
        PendingUpdateApplicator.ApplyUpdates(merged, last!);
        last!.Dispose();

        (await Names(engine)).Should().Be("c");
    }

    [Fact]
    public async Task A_conflict_between_runs_is_found_in_the_merged_list_and_nothing_changes()
    {
        // Two renames of one node, each in its own run: each run's list alone has no conflict.
        var (_, engine) = Setup();
        var merged = new PendingUpdateList();
        using var first = engine.CreateContext();
        using var second = engine.CreateContext();
        first.DeferPendingUpdates = second.DeferPendingUpdates = true;
        await ItemsAsync(Compile(engine, $"(rename node doc('{Doc}')/r/a as 'x', delete node doc('{Doc}')/r/b)"), first);
        await ItemsAsync(Compile(engine, $"rename node doc('{Doc}')/r/a as 'y'"), second);
        merged.Merge(first.PendingUpdates);
        merged.Merge(second.PendingUpdates);

        var act = () => PendingUpdateApplicator.ApplyUpdates(merged, first);

        act.Should().Throw<XQueryRuntimeException>().Which.ErrorCode.Should().Be("XUDY0015");
        (await Names(engine)).Should().Be("a b");
    }

    [Fact]
    public async Task A_copy_modify_applies_its_updates_to_its_copy_with_or_without_it()
    {
        var (_, engine) = Setup();
        var plan = Compile(engine, $"copy $c := doc('{Doc}')/r modify delete node $c/a return string-join($c/*!name(), ' ')");
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;

        (await ItemsAsync(plan, context)).Should().Equal("b");

        context.PendingUpdates.HasUpdates.Should().BeFalse();
        (await Names(engine)).Should().Be("a b");
    }

    // ---- ApplyUpdates ----

    [Fact]
    public async Task ApplyUpdates_checks_every_target_before_it_changes_a_store()
    {
        var (store, engine) = Setup();
        store.LoadFromString("<r><a/></r>", "urn:test:ro");
        store.MarkReadOnly((XdmDocument)(await ItemsAsync(engine, "doc('urn:test:ro')"))[0]!);
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;
        await ItemsAsync(Compile(engine, $"(delete node doc('{Doc}')/r/a, delete node doc('urn:test:ro')/r/a)"), context);

        var act = () => PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);

        act.Should().Throw<XQueryRuntimeException>().Which.ErrorCode.Should().Be("XUDY9001");
        (await Names(engine)).Should().Be("a b");
        context.PendingUpdates.Primitives.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyUpdates_applies_in_the_order_of_XQUF_3_0_8_2_3()
    {
        var (_, engine) = Setup();
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;
        await ItemsAsync(Compile(engine,
            $"let $r := doc('{Doc}')/r return (insert node <l/> as last into $r, insert node <i/> into $r)"), context);

        PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);

        (await Names(engine)).Should().Be("a b i l");
    }

    // ---- IsUpdating ----

    [Theory]
    [InlineData("delete node doc('urn:test:d')/r/a", true)]
    [InlineData("if (doc('urn:test:d')/r/c) then delete node doc('urn:test:d')/r/a else ()", true)]
    [InlineData("for $x in doc('urn:test:d')/r/* return delete node $x", true)]
    [InlineData("(delete node doc('urn:test:d')/r/a, ())", true)]
    [InlineData("declare %updating function local:del($n) { delete node $n }; local:del(doc('urn:test:d')/r/a)", true)]
    [InlineData("let $f := upper-case#1 return invoke updating $f('a')", true)]
    [InlineData("1", false)]
    [InlineData("copy $c := doc('urn:test:d')/r modify delete node $c/a return $c", false)]
    [InlineData("declare %updating function local:del($n) { delete node $n }; local:del#1", false)]
    [InlineData("declare %updating function local:del($n) { delete node $n }; 1", false)]
    // A call is matched by its expanded name, resolved as the function resolver resolves it: put
    // with no prefix and Q{...}put were not fn:put, so their queries were not updating.
    [InlineData("fn:put(<a/>, 'file:///tmp/x.xml')", true)]
    [InlineData("put(<a/>, 'file:///tmp/x.xml')", true)]
    [InlineData("put(<a/>, 'file:///tmp/x.xml', ())", true)]
    [InlineData("Q{http://www.w3.org/2005/xpath-functions}put(<a/>, 'file:///tmp/x.xml')", true)]
    [InlineData("declare namespace f = 'http://www.w3.org/2005/xpath-functions'; f:put(<a/>, 'file:///tmp/x.xml')", true)]
    [InlineData("put#2", false)]
    [InlineData("declare %updating function Q{urn:x}del($n) { delete node $n }; Q{urn:x}del(doc('urn:test:d')/r/a)", true)]
    [InlineData("declare namespace p = 'urn:x'; declare %updating function Q{urn:x}del($n) { delete node $n }; p:del(doc('urn:test:d')/r/a)", true)]
    [InlineData("declare namespace p = 'urn:x'; declare %updating function p:del($n) { delete node $n }; Q{urn:x}del(doc('urn:test:d')/r/a)", true)]
    // Two functions with the same local name in two namespaces are two functions.
    [InlineData("declare %updating function Q{urn:a}f() { () }; declare function Q{urn:b}f() { 1 }; Q{urn:b}f()", false)]
    [InlineData("declare %updating function Q{urn:a}f() { () }; declare function Q{urn:b}f() { 1 }; Q{urn:a}f()", true)]
    // With a default function namespace, a name with no prefix is in it, and put is not fn:put.
    [InlineData("declare default function namespace 'urn:d'; declare function Q{urn:d}put($n, $u) { 1 }; put(<a/>, 'x')", false)]
    [InlineData("declare default function namespace 'urn:d'; declare %updating function Q{urn:d}put($n, $u) { () }; put(<a/>, 'x')", true)]
    public void IsUpdating_is_the_category_of_the_body_of_the_main_module(string query, bool updating)
    {
        var (_, engine) = Setup();

        Compile(engine, query).IsUpdating.Should().Be(updating);
    }

    // ---- NodeCount ----

    [Fact]
    public async Task NodeCount_counts_the_nodes_loaded_built_and_copied()
    {
        var (store, engine) = Setup();
        // The document node, r, a, its text, b, its text.
        store.NodeCount.Should().Be(6);

        // e, f, and the copy of f that the constructor of e makes its child.
        await RunAsync(engine, "<e><f/></e>");
        store.NodeCount.Should().Be(9);

        // The constructed n, and the copy of it that is inserted (XQUF 3.0 §3.2.1).
        await RunAsync(engine, $"insert node <n/> into doc('{Doc}')/r");
        store.NodeCount.Should().Be(11);
        (await Names(engine)).Should().Be("a b n");
    }

    [Fact]
    public async Task NodeCount_goes_down_by_one_for_a_deleted_element_whose_descendants_stay()
    {
        var (store, engine) = Setup();

        await RunAsync(engine, $"delete node doc('{Doc}')/r/a");

        store.NodeCount.Should().Be(5);
        (await Names(engine)).Should().Be("b");
    }

    // ---- A node of another store ----

    public static TheoryData<string, string, string> ForeignInserts => new()
    {
        { "insert-into", "<x xmlns:p=\"urn:p\"><p:y z=\"1\">t</p:y></x>", "<r><a k=\"v\">a<x xmlns:p=\"urn:p\"><p:y z=\"1\">t</p:y></x></a><b>b</b></r>" },
        { "insert-attribute", "attribute z { '1' }", "<r><a k=\"v\" z=\"1\">a</a><b>b</b></r>" },
        { "replace-attribute", "attribute z { '1' }", "<r><a z=\"1\">a</a><b>b</b></r>" },
        { "replace-node", "<x xmlns:p=\"urn:p\"><p:y z=\"1\">t</p:y></x>", "<r><a k=\"v\">a</a><x xmlns:p=\"urn:p\"><p:y z=\"1\">t</p:y></x></r>" },
    };

    [Theory]
    [MemberData(nameof(ForeignInserts))]
    public async Task A_node_of_another_store_is_copied_into_the_store_it_goes_to(string update, string content, string result)
    {
        // Each store numbers its nodes from 1: the node built in the other store has the id of a
        // node of this one.
        var (store, engine) = Setup(xml: "<r><a k='v'>a</a><b>b</b></r>");
        var other = new XdmDocumentStore();
        var node = (XdmNode)(await ItemsAsync(new QueryEngine(nodeProvider: other, documentResolver: other), content))[0]!;
        node.Store.Should().BeSameAs(other);
        store.GetNode(node.Id).Should().NotBeNull("the ids of the two stores overlap");
        var a = (XdmElement)(await ItemsAsync(engine, $"doc('{Doc}')/r/a"))[0]!;
        var k = (XdmNode)(await ItemsAsync(engine, $"doc('{Doc}')/r/a/@k"))[0]!;
        var b = (XdmNode)(await ItemsAsync(engine, $"doc('{Doc}')/r/b"))[0]!;
        var pul = new PendingUpdateList();
        switch (update)
        {
            case "insert-into": pul.AddInsert(a, node, InsertPosition.AsLastInto); break;
            case "insert-attribute": pul.AddInsert(a, node, InsertPosition.Into); break;
            case "replace-attribute": pul.AddReplaceNode(k, node); break;
            case "replace-node": pul.AddReplaceNode(b, node); break;
        }
        using var context = engine.CreateContext();

        // Bounded: with the node registered as it was, or with its parent set before it was added,
        // the update or the serialization did not end.
        var serialized = await Task.Run(async () =>
        {
            PendingUpdateApplicator.ApplyUpdates(pul, context);
            return await Serialized(engine);
        }).WaitAsync(TimeSpan.FromSeconds(30));

        serialized.Should().Be(result);
        node.Store.Should().BeSameAs(other);
    }
}
