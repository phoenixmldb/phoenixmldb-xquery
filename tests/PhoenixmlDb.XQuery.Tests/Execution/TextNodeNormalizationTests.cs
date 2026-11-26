using AwesomeAssertions;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// upd:applyUpdates (XQUF 3.0 §8.2.3), after the primitives: "If, as a net result of the above steps,
/// the children property of some node contains adjacent text nodes, these adjacent text nodes are
/// merged into a single text node. The string-value of the resulting text node is the concatenated
/// string-values of the adjacent text nodes, with no intervening space added." and "If, as a net
/// result of the above steps, the children property of some node contains an empty text node, that
/// empty text node is deleted from the children property." Neither was done, by any primitive: a
/// delete between two text nodes left two, a replace value of a text node with "" left an empty one.
/// The first text node of a run remains, with its id; the others leave the store.
/// </summary>
public class TextNodeNormalizationTests
{
    private const string Doc = "urn:test:t";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup(string xml)
    {
        var store = new XdmDocumentStore();
        store.LoadFromString(xml, Doc);
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

    private static async Task<PendingUpdateList> DeferAsync(QueryEngine engine, string update)
    {
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;
        await foreach (var _ in Compile(engine, update).ExecuteAsync(context)) { }
        return context.PendingUpdates;
    }

    private static async Task UpdateAsync(QueryEngine engine, string update, bool deferred)
    {
        if (!deferred)
        {
            await RunAsync(engine, update);
            return;
        }
        using var context = engine.CreateContext();
        PendingUpdateApplicator.ApplyUpdates(await DeferAsync(engine, update), context);
    }

    private static string Children(string path)
        => $"string-join({path}/node() ! (if (. instance of element()) then 'E:' || name(.) else 'T:' || string(.)), ',')";

    public static TheoryData<string, string, string, bool> Primitives()
    {
        var data = new TheoryData<string, string, string, bool>();
        foreach (var deferred in new[] { false, true })
        {
            data.Add("<r>a<b/>c</r>", $"delete node doc('{Doc}')/r/b", "T:ac", deferred);
            data.Add("<r>a<b/></r>", $"replace node doc('{Doc}')/r/b with text {{ 'c' }}", "T:ac", deferred);
            data.Add("<r>a<b/></r>", $"replace node doc('{Doc}')/r/b with 'c'", "T:ac", deferred);
            data.Add("<r>a<b/>c</r>", $"replace node doc('{Doc}')/r/b with ()", "T:ac", deferred);
            data.Add("<r>a<b/></r>", $"replace value of node doc('{Doc}')/r/text() with ''", "E:b", deferred);
            data.Add("<r>a<b/></r>", $"replace value of node doc('{Doc}')/r with ''", "", deferred);
            data.Add("<r>a<b/></r>", $"insert node 'x' after doc('{Doc}')/r/text()", "T:ax,E:b", deferred);
            data.Add("<r><b/>c</r>", $"insert node text {{ 'x' }} as first into doc('{Doc}')/r", "T:x,E:b,T:c", deferred);
            data.Add("<r>a<b/>c</r>", $"(delete node doc('{Doc}')/r/b, insert node 'x' into doc('{Doc}')/r)", "T:acx", deferred);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Primitives))]
    public async Task Adjacent_text_nodes_are_one_and_an_empty_one_is_deleted(string xml, string update, string children, bool deferred)
    {
        var (_, engine) = Setup(xml);

        await UpdateAsync(engine, update, deferred);

        (await RunAsync(engine, Children($"doc('{Doc}')/r"))).Should().Be(children);
    }

    // Two inserts at one place in one list: their order is that of the list; they are one text node.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_inserts_at_one_place_in_one_list_are_one_text_node(bool deferred)
    {
        var (_, engine) = Setup("<r/>");

        await UpdateAsync(engine, $"(insert node text {{ 'p' }} before doc('{Doc}')/r, insert node 'x' before doc('{Doc}')/r)", deferred);

        (await RunAsync(engine, Children($"doc('{Doc}')"))).Should().Be("T:px,E:r");
    }

    [Fact]
    public async Task The_lists_of_two_runs_applied_together_make_one_text_node()
    {
        var (_, engine) = Setup("<r/>");
        var first = await DeferAsync(engine, $"insert node text {{ 'p' }} before doc('{Doc}')/r");
        var second = await DeferAsync(engine, $"insert node 'x' before doc('{Doc}')/r");
        first.Merge(second);

        using var context = engine.CreateContext();
        PendingUpdateApplicator.ApplyUpdates(first, context);

        (await RunAsync(engine, Children($"doc('{Doc}')"))).Should().Be("T:px,E:r");
    }

    // Two separate lists, each applied: the second merges with the text node the first left.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_text_node_inserted_next_to_one_already_there_is_merged_with_it(bool deferred)
    {
        var (_, engine) = Setup("<r/>");
        await UpdateAsync(engine, $"insert node text {{ 'p' }} before doc('{Doc}')/r", deferred);

        await UpdateAsync(engine, $"insert node 'x' after doc('{Doc}')/node()[1]", deferred);

        (await RunAsync(engine, Children($"doc('{Doc}')"))).Should().Be("T:px,E:r");
    }

    // Which node remains: the first of the run, with its id and the merged value; the second, and
    // the deleted element, leave the store.
    [Fact]
    public async Task The_first_text_node_remains_with_its_id_and_the_others_leave_the_store()
    {
        var (store, engine) = Setup("<r>a<b/>c</r>");
        var texts = (await ItemsAsync(engine, $"doc('{Doc}')/r/text()")).Cast<XdmText>().ToList();
        var count = store.NodeCount;

        await UpdateAsync(engine, $"delete node doc('{Doc}')/r/b", deferred: true);

        store.GetNode(texts[0].Id).Should().BeOfType<XdmText>().Which.Value.Should().Be("ac");
        store.GetNode(texts[1].Id).Should().BeNull();
        store.NodeCount.Should().Be(count - 2, "the element and the second text node left the store");
    }

    // A node collected for steps 3-4 that the same list removes: it has no children in the store any
    // more (GetChildren gives none, GetNode null), and is skipped. One inside a subtree that a replace
    // node removed is still in the store, reached from nothing; its children are merged with no effect
    // on the document.
    public static TheoryData<string, string, string, bool> RemovedParents()
    {
        var data = new TheoryData<string, string, string, bool>();
        foreach (var deferred in new[] { false, true })
        {
            data.Add("<root><r>a<b/>c</r></root>", $"(delete node doc('{Doc}')/root/r/b, delete node doc('{Doc}')/root/r)", "", deferred);
            data.Add("<root><r>a<b/>c</r></root>", $"(delete node doc('{Doc}')/root/r/b, replace node doc('{Doc}')/root/r with <x/>)", "E:x", deferred);
            data.Add("<root><r><s>a<b/>c</s></r></root>", $"(delete node doc('{Doc}')/root/r/s/b, replace node doc('{Doc}')/root/r with <x/>)", "E:x", deferred);
            data.Add("<root><r><s>a<b/>c</s></r></root>", $"(delete node doc('{Doc}')/root/r/s/b, delete node doc('{Doc}')/root/r)", "", deferred);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(RemovedParents))]
    public async Task A_collected_node_that_the_list_removes_is_skipped(string xml, string update, string children, bool deferred)
    {
        var (_, engine) = Setup(xml);

        await UpdateAsync(engine, update, deferred);

        (await RunAsync(engine, Children($"doc('{Doc}')/root"))).Should().Be(children);
    }

    [Theory]
    [InlineData("(delete node $c/root/r/b, delete node $c/root/r)", "")]
    [InlineData("(delete node $c/root/r/s/b, replace node $c/root/r with <x/>)", "E:x")]
    public async Task Copy_modify_skips_a_collected_node_that_the_list_removes(string update, string children)
    {
        var (_, engine) = Setup("<r/>");

        (await RunAsync(engine, $"copy $c := document {{ <root><r><s>a<b/>c</s>a<b/>c</r></root> }} modify {update} return {Children("$c/root")}"))
            .Should().Be(children);
    }

    // In memory: copy modify applies its list with the same steps.
    [Theory]
    [InlineData("delete node $c/r/b", "T:ac")]
    [InlineData("replace value of node $c/r/text()[1] with ''", "E:b,T:c")]
    [InlineData("insert node 'x' after $c/r/b", "T:a,E:b,T:xc")]
    public async Task Copy_modify_merges_and_deletes_text_nodes_too(string update, string children)
    {
        var (_, engine) = Setup("<r/>");

        (await RunAsync(engine, $"copy $c := document {{ <r>a<b/>c</r> }} modify {update} return {Children("$c/r")}"))
            .Should().Be(children);
    }
}
