using AwesomeAssertions;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// <see cref="XdmDocumentStore"/> applies an update by replacing the node object (its name,
/// children and attributes are init-only). The replacement of a renamed element, or of an element
/// whose attributes changed, had no string value: <see cref="XdmElement.StringValue"/> threw, and so
/// did the store when the same list changed the children of the element's parent, after it had
/// changed the document (<c>(delete node $r/a, rename node $r/b as 'c')</c>).
/// </summary>
public class ReplacedElementStringValueTests
{
    private const string Doc = "urn:test:d";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r><a>a</a><b k='v'>b</b></r>", Doc);
        return (store, new QueryEngine(nodeProvider: store, documentResolver: store));
    }

    private static async Task<List<object?>> ItemsAsync(QueryEngine engine, string query)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var items = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(compiled.ExecutionPlan!))
            items.Add(item);
        return items;
    }

    public static TheoryData<string, string, string> Updates => new()
    {
        // The element is replaced, its children are not changed; the children of its parent are.
        { "(delete node $r/a, rename node $r/b as 'c')", "<r><c k=\"v\">b</c></r>", "b" },
        { "(rename node $r/b as 'c', insert node <x>x</x> into $r)", "<r><a>a</a><c k=\"v\">b</c><x>x</x></r>", "abx" },
        { "(insert node attribute z { '1' } into $r/b, delete node $r/a)", "<r><b k=\"v\" z=\"1\">b</b></r>", "b" },
        { "(delete node $r/b/@k, delete node $r/a)", "<r><b>b</b></r>", "b" },
        { "(rename node $r/b/@k as 'j', delete node $r/a)", "<r><b j=\"v\">b</b></r>", "b" },
        { "(replace value of node $r/b/@k with 'w', delete node $r/a)", "<r><b k=\"w\">b</b></r>", "b" },
        // Its own children change too: computed again.
        { "(rename node $r/b as 'c', replace value of node $r/b with 'y', delete node $r/a)", "<r><c k=\"v\">y</c></r>", "y" },
        { "(rename node $r/b as 'c', insert node <x>x</x> into $r/b)", "<r><a>a</a><c k=\"v\">b<x>x</x></c></r>", "abx" },
    };

    [Theory]
    [MemberData(nameof(Updates))]
    public async Task An_element_an_update_replaced_keeps_its_string_value(string updates, string document, string value)
    {
        var (_, engine) = Setup();

        await ItemsAsync(engine, $"let $r := doc('{Doc}')/r return {updates}");

        (await ItemsAsync(engine, $"serialize(doc('{Doc}'))")).Should().Equal(document);
        var r = (XdmElement)(await ItemsAsync(engine, $"doc('{Doc}')/r"))[0]!;
        r.StringValue.Should().Be(value);
        var children = (await ItemsAsync(engine, $"doc('{Doc}')/r/*")).Cast<XdmElement>().ToList();
        string.Concat(children.Select(c => c.StringValue)).Should().Be(value);
        ((XdmDocument)(await ItemsAsync(engine, $"doc('{Doc}')"))[0]!).StringValue.Should().Be(value);
    }

    [Fact]
    public async Task A_renamed_element_keeps_its_string_value()
    {
        var (_, engine) = Setup();

        await ItemsAsync(engine, $"rename node doc('{Doc}')/r/b as 'c'");

        ((XdmElement)(await ItemsAsync(engine, $"doc('{Doc}')/r/c"))[0]!).StringValue.Should().Be("b");
    }
}
