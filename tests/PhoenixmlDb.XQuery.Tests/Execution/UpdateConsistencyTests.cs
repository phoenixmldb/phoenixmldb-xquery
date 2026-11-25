using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// What the updates of a query may not do to an element (XQUF 3.0 §5.1, §5.3.1, §5.4,
/// upd:applyUpdates): give it two attributes of one name (XUDY0021), bind a prefix that its
/// namespaces bind to another URI (XUDY0023), or bind one prefix to two URIs (XUDY0024). None was
/// checked: the second attribute was added beside the first, and a conflicting prefix was written.
/// </summary>
public class UpdateConsistencyTests
{
    private const string D = "doc('urn:test:d')";

    private static QueryEngine Setup()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r xmlns:p='urn:p'><a x='1'/><b/></r>", "urn:test:d");
        return new QueryEngine(nodeProvider: store, documentResolver: store);
    }

    private static async Task<string> RunAsync(QueryEngine engine, string query)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var items = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(compiled.ExecutionPlan!))
            items.Add(item);
        return string.Join(" ", items.Select(i => i?.ToString()));
    }

    // Each child of r: its name and its attributes.
    private static Task<string> Document(QueryEngine engine)
        => RunAsync(engine, $"string-join({D}/r/*!(name() || '[' || string-join(@*!(name() || '=' || .), ',') || ']'), ' ')");

    [Theory]
    // Two attributes of one name (upd:applyUpdates)
    [InlineData($"insert node attribute x {{ 2 }} into {D}/r/a", "XUDY0021")]
    [InlineData($"(insert node attribute y {{ 1 }} into {D}/r/b, insert node attribute y {{ 2 }} into {D}/r/b)", "XUDY0021")]
    [InlineData($"(rename node {D}/r/a/@x as 'z', insert node attribute z {{ 1 }} into {D}/r/a)", "XUDY0021")]
    // A binding that conflicts with the element's namespaces (§5.1, §5.3.1, §5.4)
    [InlineData($"insert node attribute {{ QName('urn:other', 'p:q') }} {{ 1 }} into {D}/r/a", "XUDY0023")]
    [InlineData($"replace node {D}/r/a/@x with attribute {{ QName('urn:other', 'p:x') }} {{ 1 }}", "XUDY0023")]
    [InlineData($"rename node {D}/r/a as QName('urn:other', 'p:a')", "XUDY0023")]
    [InlineData($"rename node {D}/r/a/@x as QName('urn:other', 'p:x')", "XUDY0023")]
    // One prefix bound to two URIs on one element, by one expression or two
    [InlineData($"insert node (attribute {{ QName('urn:1', 'n:a') }} {{ 1 }}, attribute {{ QName('urn:2', 'n:b') }} {{ 2 }}) into {D}/r/b", "XUDY0024")]
    [InlineData($"(insert node attribute {{ QName('urn:1', 'n:a') }} {{ 1 }} into {D}/r/b, rename node {D}/r/b as QName('urn:2', 'n:b'))", "XUDY0024")]
    public async Task An_update_that_would_make_an_element_inconsistent_is_an_error_and_nothing_changes(string update, string code)
    {
        var engine = Setup();

        var act = () => RunAsync(engine, update);

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be(code);
        (await Document(engine)).Should().Be("a[x=1] b[]");
    }

    [Theory]
    // An attribute replaced, or deleted and inserted again, by one of the same name
    [InlineData($"replace node {D}/r/a/@x with attribute x {{ 9 }}", "a[x=9] b[]")]
    [InlineData($"(delete node {D}/r/a/@x, insert node attribute x {{ 5 }} into {D}/r/a)", "a[x=5] b[]")]
    // The prefix the element's namespaces bind, to the same URI
    [InlineData($"insert node attribute {{ QName('urn:p', 'p:q') }} {{ 1 }} into {D}/r/a", "a[x=1,p:q=1] b[]")]
    // One prefix, one URI, from two expressions
    [InlineData($"(insert node attribute {{ QName('urn:1', 'n:a') }} {{ 1 }} into {D}/r/b, insert node attribute {{ QName('urn:1', 'n:c') }} {{ 2 }} into {D}/r/b)", "a[x=1] b[n:a=1,n:c=2]")]
    public async Task An_update_that_keeps_the_element_consistent_is_made(string update, string document)
    {
        var engine = Setup();

        (await RunAsync(engine, update)).Should().BeEmpty();

        (await Document(engine)).Should().Be(document);
    }
}
