using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A document node need not be well-formed: document{&lt;a/&gt;, &lt;b/&gt;} has two element
/// children. The XML output method serializes its children. The serializer used .NET's Document
/// conformance for every document node, which rejects a second root element, so the result was
/// empty (QT3 K2-ConDocNode-9, -10).
/// </summary>
public class MultiRootDocumentSerializationTests
{
    private static async Task<string> Serialize(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        items.Should().ContainSingle();
        return XQueryResultSerializer.Serialize(items[0], store, OutputMethod.Xml);
    }

    [Theory]
    [InlineData("document{<a/>, <b/>, <c/>}", "<a/><b/><c/>")]
    [InlineData("document{<!--c-->}", "<!--c-->")]
    [InlineData("document{<a/>, 'x'}", "<a/>x")]
    public async Task A_document_without_one_root_element_serializes_its_children(string query, string expected)
        => (await Serialize(query)).Replace("\n", "", StringComparison.Ordinal).Should().EndWith("?>" + expected);

    [Fact]
    public async Task A_well_formed_document_still_serializes_as_a_document()
        => (await Serialize("document{<!--c-->, <a/>}")).Should().EndWith("?><!--c--><a/>");
}
