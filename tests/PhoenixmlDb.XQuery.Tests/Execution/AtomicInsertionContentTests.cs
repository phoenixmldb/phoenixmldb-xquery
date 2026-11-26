using AwesomeAssertions;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Atomic values in an insertion or replacement sequence. XQUF 3.0 §5.1 and §5.3.1: the sequence
/// "is evaluated as though it were an enclosed expression in an element constructor (see Rule 1e
/// in Section 3.9.1.3 Content)", so each run of adjacent atomic values is one text node, its values
/// separated by a single space. The values were kept as they were, and only nodes are placed when
/// the list is applied: <c>insert node 'text' before doc('a')/r</c>, an atomic value into an element
/// and <c>replace node ... with 'x'</c> changed nothing, with no error (3.2.0 too). upd:applyUpdates
/// (step 3) merges the text nodes that are then adjacent.
/// </summary>
public class AtomicInsertionContentTests
{
    private const string Doc = "urn:test:a";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup(string xml = "<r/>")
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

    private static async Task<string> RunAsync(QueryEngine engine, string query)
    {
        var items = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(Compile(engine, query)))
            items.Add(item);
        return string.Join(" ", items.Select(i => i?.ToString()));
    }

    // The normal path applies the list when the run ends; the deferred one leaves it to the host.
    private static async Task UpdateAsync(QueryEngine engine, string update, bool deferred)
    {
        if (!deferred)
        {
            await RunAsync(engine, update);
            return;
        }
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;
        await foreach (var _ in Compile(engine, update).ExecuteAsync(context)) { }
        context.PendingUpdates.HasUpdates.Should().BeTrue();
        PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);
    }

    // The children of a node: E:name for an element, T:value for a text node.
    private static string Children(string path)
        => $"string-join({path}/node() ! (if (. instance of element()) then 'E:' || name(.) else 'T:' || string(.)), ',')";

    public static TheoryData<string, string, bool> DocumentNode()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var deferred in new[] { false, true })
        {
            data.Add($"insert node 'text' before doc('{Doc}')/r", "T:text,E:r", deferred);
            data.Add($"insert node 'text' after doc('{Doc}')/r", "E:r,T:text", deferred);
            data.Add($"insert node 'text' into doc('{Doc}')", "E:r,T:text", deferred);
            data.Add($"insert node ('a', 1) before doc('{Doc}')/r", "T:a 1,E:r", deferred);
            data.Add($"insert node ('a', 1) after doc('{Doc}')/r", "E:r,T:a 1", deferred);
            data.Add($"insert node ('a', 1) as first into doc('{Doc}')", "T:a 1,E:r", deferred);
            data.Add($"insert node ('a', 1) as last into doc('{Doc}')", "E:r,T:a 1", deferred);
            data.Add($"insert node ('a', 1) into doc('{Doc}')", "E:r,T:a 1", deferred);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(DocumentNode))]
    public async Task Atomic_values_are_a_text_node_child_of_the_document_node(string update, string children, bool deferred)
    {
        var (_, engine) = Setup();

        await UpdateAsync(engine, update, deferred);

        (await RunAsync(engine, Children($"doc('{Doc}')"))).Should().Be(children);
        (await RunAsync(engine, $"string(doc('{Doc}'))")).Should().Be(children.Contains("a 1") ? "a 1" : "text");
    }

    public static TheoryData<string, string, bool> ElementNode()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var deferred in new[] { false, true })
        {
            data.Add($"insert node 'x' into doc('{Doc}')/r", "E:c,T:x", deferred);
            data.Add($"insert node ('a', 1) as first into doc('{Doc}')/r", "T:a 1,E:c", deferred);
            data.Add($"insert node 'x' before doc('{Doc}')/r/c", "T:x,E:c", deferred);
            data.Add($"insert node ('a', <e/>, 'b') after doc('{Doc}')/r/c", "E:c,T:a,E:e,T:b", deferred);
            data.Add($"insert node ('a', '', 'b') into doc('{Doc}')/r", "E:c,T:a  b", deferred);
            data.Add($"insert node ('', 'a', '') into doc('{Doc}')/r", "E:c,T: a ", deferred);
            data.Add($"replace node doc('{Doc}')/r/c with ('x', 2)", "T:x 2", deferred);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ElementNode))]
    public async Task Atomic_values_are_a_text_node_child_of_an_element(string update, string children, bool deferred)
    {
        var (_, engine) = Setup("<r><c/></r>");

        await UpdateAsync(engine, update, deferred);

        (await RunAsync(engine, Children($"doc('{Doc}')/r"))).Should().Be(children);
    }

    // The rest of rule 1e and §5.1. XQuery 3.1 §3.9.1.3 rule 1e: "Each array returned by the enclosed
    // expression is flattened"; "If an enclosed expression returns a function, a type error is raised
    // [err:XQTY0105]". XQUF 3.0 §5.1: "If the insertion sequence contains a document node, the
    // document node is replaced in the insertion sequence by its children." Adjacent text nodes are
    // one text node and an empty one is dropped (upd:applyUpdates steps 3 and 4).
    public static TheoryData<string, string, bool> ContentRule()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var deferred in new[] { false, true })
        {
            data.Add($"insert node [ 'a', <b/> ] into doc('{Doc}')/r", "E:c,T:a,E:b", deferred);
            data.Add($"insert node ([ <b/>, [ 'x', 'y' ] ], 'z') into doc('{Doc}')/r", "E:c,E:b,T:x y z", deferred);
            data.Add($"replace node doc('{Doc}')/r/c with [ <b/> ]", "E:b", deferred);
            data.Add($"insert node document {{ <x/>, <y/> }} before doc('{Doc}')/r/c", "E:x,E:y,E:c", deferred);
            data.Add($"insert node doc('{Other}') into doc('{Doc}')/r", "E:c,E:o", deferred);
            data.Add($"insert node (text {{ 'a' }}, text {{ '' }}, text {{ 'b' }}) into doc('{Doc}')/r", "E:c,T:ab", deferred);
            data.Add($"insert node (text {{ 'a' }}, 'b', 'c', text {{ 'd' }}) into doc('{Doc}')/r", "E:c,T:ab cd", deferred);
        }
        return data;
    }

    private const string Other = "urn:test:o";

    [Theory]
    [MemberData(nameof(ContentRule))]
    public async Task The_content_is_element_content(string update, string children, bool deferred)
    {
        var (store, engine) = Setup("<r><c/></r>");
        store.LoadFromString("<o/>", Other);

        await UpdateAsync(engine, update, deferred);

        (await RunAsync(engine, Children($"doc('{Doc}')/r"))).Should().Be(children);
    }

    [Theory]
    [InlineData("insert node map { } into doc('" + Doc + "')/r")]
    [InlineData("insert node (<x/>, true#0) into doc('" + Doc + "')/r")]
    [InlineData("insert node [ map { 'a': 1 } ] before doc('" + Doc + "')/r/c")]
    [InlineData("replace node doc('" + Doc + "')/r/c with map { }")]
    public async Task A_function_item_in_the_content_is_XQTY0105(string update)
    {
        var (_, engine) = Setup("<r><c/></r>");

        var act = () => RunAsync(engine, update);

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XQTY0105");
        (await RunAsync(engine, Children($"doc('{Doc}')/r"))).Should().Be("E:c");
    }

    // XQUF 3.0 §5.1: an attribute after other content of the insertion sequence is XUTY0004 (a guard).
    [Fact]
    public async Task An_attribute_after_other_content_is_XUTY0004()
    {
        var (_, engine) = Setup("<r><c/></r>");

        var act = () => RunAsync(engine, $"insert node ('a', attribute b {{ 'v' }}) into doc('{Doc}')/r");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUTY0004");
    }

    // An empty string makes no text node (rule 1e gives "", which upd:applyUpdates deletes).
    [Fact]
    public async Task An_empty_string_inserts_nothing()
    {
        var (_, engine) = Setup("<r><c/></r>");

        await UpdateAsync(engine, $"insert node '' into doc('{Doc}')/r", deferred: false);

        (await RunAsync(engine, Children($"doc('{Doc}')/r"))).Should().Be("E:c");
    }

    // upd:applyUpdates step 3: text nodes that the update makes adjacent are merged into one.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_text_node_inserted_next_to_one_is_merged_with_it(bool deferred)
    {
        var (_, engine) = Setup();
        await UpdateAsync(engine, $"insert node text {{ 'p' }} before doc('{Doc}')/r", deferred);

        await UpdateAsync(engine, $"insert node 'x' before doc('{Doc}')/r", deferred);

        (await RunAsync(engine, Children($"doc('{Doc}')"))).Should().Be("T:px,E:r");
    }

    // In memory: the overlay of copy modify takes the same content.
    [Theory]
    [InlineData("insert node 'x' before $d/r", "T:x,E:r")]
    [InlineData("insert node ('a', 1) before $d/r", "T:a 1,E:r")]
    [InlineData("insert node ('a', 1) as last into $d", "E:r,T:a 1")]
    [InlineData("insert node document { <x/> } before $d/r", "E:x,E:r")]
    [InlineData("insert node [ 'a', <b/> ] before $d/r", "T:a,E:b,E:r")]
    [InlineData("insert node (text { 'a' }, text { 'b' }) after $d/r", "E:r,T:ab")]
    public async Task Copy_modify_takes_atomic_values_as_text(string update, string children)
    {
        var (_, engine) = Setup();

        (await RunAsync(engine, $"copy $d := document {{ <r/> }} modify {update} return {Children("$d")}"))
            .Should().Be(children);
    }

    [Fact]
    public async Task Copy_modify_into_an_element_takes_atomic_values_as_text()
    {
        var (_, engine) = Setup();

        (await RunAsync(engine, $"copy $d := document {{ <r/> }} modify insert node ('a', 1) into $d/r return string($d/r)"))
            .Should().Be("a 1");
    }

    // The note on ApplyUpdates: a document node whose children change is a new instance in the
    // store; the one held before the call is not changed.
    [Fact]
    public async Task The_store_gives_a_new_document_node_after_its_children_change()
    {
        var (store, engine) = Setup();
        var before = store.ResolveDocument(Doc)!;

        await UpdateAsync(engine, $"insert node 'x' before doc('{Doc}')/r", deferred: true);

        var after = store.GetNode(before.Id).Should().BeOfType<XdmDocument>().Subject;
        after.Should().NotBeSameAs(before);
        after.Children.Should().HaveCount(2);
        before.Children.Should().ContainSingle();
        store.ResolveDocument(Doc).Should().BeSameAs(after);
    }
}
