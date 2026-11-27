using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A node constructor needs a node store that builds nodes (<see cref="INodeBuilder"/>). With none
/// (a <see cref="QueryEngine"/> made with no node provider, or a provider that only reads nodes),
/// every constructor throws the same <see cref="InvalidOperationException"/>.
/// </summary>
/// <remarks>
/// In 3.2.0 the text, comment, processing-instruction, attribute and namespace constructors then made
/// a node with the id 0 (<see cref="NodeId.None"/>), which no store can find, and an attribute lost
/// its namespace; the element and document constructors already threw this exception. A host that
/// constructs nodes gives the engine a node provider that builds them, such as an
/// <see cref="XdmDocumentStore"/>.
/// </remarks>
public class ConstructorNodeBuilderTests
{
    public static TheoryData<string, string> Constructors() => new()
    {
        { "text { 'x' }", "A text constructor" },
        { "comment { 'x' }", "A comment constructor" },
        { "<!--x-->", "A comment constructor" },
        { "processing-instruction p { 'x' }", "A processing-instruction constructor" },
        { "processing-instruction { 'p' } { 'x' }", "A processing-instruction constructor" },
        { "<?p x?>", "A processing-instruction constructor" },
        { "attribute a { 'x' }", "An attribute constructor" },
        { "attribute { 'a' } { 'x' }", "An attribute constructor" },
        { "attribute Q{urn:q}a { 'x' }", "An attribute constructor" },
        { "namespace p { 'urn:x' }", "A namespace constructor" },
        { "element e { 'x' }", "An element constructor" },
        { "<e>x</e>", "An element constructor" },
        { "document { 'x' }", "A document constructor" },
    };

    private static async Task<List<object?>> RunAsync(QueryEngine engine, string query)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var items = new List<object?>();
        using var context = engine.CreateContext();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(context))
            items.Add(item);
        return items;
    }

    [Theory]
    [MemberData(nameof(Constructors))]
    public async Task With_no_store_that_builds_nodes_a_constructor_throws(string query, string constructor)
    {
        foreach (var asString in new[] { false, true })
        {
            var error = await Record.ExceptionAsync(() => RunAsync(new QueryEngine(), asString ? $"string(({query}))" : query)).ConfigureAwait(true);
            error.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().StartWith($"{constructor} requires a node store implementing INodeBuilder, but the host supplied no node store.");
        }
    }

    [Theory]
    [MemberData(nameof(Constructors))]
    public async Task With_a_store_that_builds_nodes_a_constructor_works(string query, string constructor)
    {
        _ = constructor;
        var store = new XdmDocumentStore();
        var items = await RunAsync(new QueryEngine(nodeProvider: store, documentResolver: store), query).ConfigureAwait(true);
        var node = items.Should().ContainSingle().Which.Should().BeAssignableTo<Xdm.Nodes.XdmNode>().Subject;
        node.Id.Should().NotBe(NodeId.None);
    }

    /// <summary>
    /// The attribute of a prefixed name keeps its namespace: with no builder it was in no namespace.
    /// </summary>
    [Fact]
    public async Task An_attribute_keeps_its_namespace()
    {
        var store = new XdmDocumentStore();
        var items = await RunAsync(new QueryEngine(nodeProvider: store, documentResolver: store),
            "declare namespace q = 'urn:q'; namespace-uri(attribute q:a { 'x' })").ConfigureAwait(true);
        items.Should().ContainSingle().Which!.ToString().Should().Be("urn:q");
    }

    /// <summary>Guards: a query that constructs no node needs no store.</summary>
    [Theory]
    [InlineData("1 + 1", "2")]
    [InlineData("string-join(('a', 'b'), '-')", "a-b")]
    [InlineData("count(text { () })", "0")]
    public async Task With_no_store_a_query_that_constructs_nothing_works(string query, string expected)
    {
        var items = await RunAsync(new QueryEngine(), query).ConfigureAwait(true);
        items.Should().ContainSingle().Which!.ToString().Should().Be(expected);
    }
}
