using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XQUF 3.0 §8.2.3: upd:applyUpdates applies upd:insertInto (with upd:insertAttributes,
/// upd:replaceValue and upd:rename) first; then upd:insertBefore, upd:insertAfter,
/// upd:insertIntoAsFirst and upd:insertIntoAsLast; then upd:replaceNode; then
/// upd:replaceElementContent; then upd:delete. The inserts were applied in the order of the list, so
/// a node inserted "as last" by an earlier expression came before one inserted "into" the same
/// element by a later one (W3C XQUTS id-insert-expr-052). And in a copy (copy-modify),
/// "replace value of node" of an element kept the element's element, comment and processing
/// instruction children (id-transform-expr-025): upd:replaceElementContent replaces them all.
/// </summary>
public class UpdateApplyOrderTests
{
    private const string Doc = "urn:test:d";

    private static async Task<string> RunAsync(QueryEngine engine, string query)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var items = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(compiled.ExecutionPlan!))
            items.Add(item);
        return string.Join(" ", items.Select(i => i?.ToString()));
    }

    public static TheoryData<string, string> Inserts => new()
    {
        // "into" before "as last", whatever the order of the expressions.
        { "insert node <l/> as last into $r, insert node <i/> into $r", "a b i l" },
        { "insert node <i/> into $r, insert node <l/> as last into $r", "a b i l" },
        // The same with more of the kinds of phase 2 (XQUTS id-insert-expr-052 in short).
        { "insert node <f/> as first into $r, insert node <bb/> before $r/b, insert node <l/> as last into $r, insert node <i/> into $r", "f a bb b i l" },
    };

    [Theory]
    [MemberData(nameof(Inserts))]
    public async Task Inserts_into_an_element_come_before_those_as_last_in_a_stored_document(string updates, string names)
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r><a/><b/></r>", Doc);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);

        await RunAsync(engine, $"let $r := doc('{Doc}')/r return ({updates})");

        (await RunAsync(engine, $"string-join(doc('{Doc}')/r/*!name(), ' ')")).Should().Be(names);
    }

    [Theory]
    [MemberData(nameof(Inserts))]
    public async Task Inserts_into_an_element_come_before_those_as_last_in_a_copy(string updates, string names)
        => (await new XQueryFacade().EvaluateAsync($"copy $r := <r><a/><b/></r> modify ({updates}) return string-join($r/*!name(), ' ')"))
            .Should().Be(names);

    public static TheoryData<string, string> ElementContents => new()
    {
        // The children are replaced, all of them.
        { "replace value of node $e with 'v'", "<e>v</e>" },
        { "replace value of node $e with ''", "<e/>" },
        // Inserted into the element first (phase 1), then replaced with its content (phase 4).
        { "insert node <x/> into $e, replace value of node $e with 'v'", "<e>v</e>" },
        // replaceNode (phase 3) before replaceElementContent (phase 4).
        { "replace node $e/a with <y/>, replace value of node $e with 'v'", "<e>v</e>" },
    };

    [Theory]
    [MemberData(nameof(ElementContents))]
    public async Task Replace_value_of_an_element_in_a_copy_replaces_all_its_children(string updates, string result)
        => (await new XQueryFacade().EvaluateAsync($"copy $e := <e><a/>t<!--c--><?p p?><b>B</b></e> modify ({updates}) return serialize($e)"))
            .Should().Be(result);

    [Theory]
    [MemberData(nameof(ElementContents))]
    public async Task Replace_value_of_an_element_in_a_stored_document_replaces_all_its_children(string updates, string result)
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<e><a/>t<!--c--><?p p?><b>B</b></e>", Doc);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);

        await RunAsync(engine, $"let $e := doc('{Doc}')/e return ({updates})");

        (await RunAsync(engine, $"serialize(doc('{Doc}')/e)")).Should().Be(result);
    }
}
