using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A store shared across queries must not let one compilation's pre-allocated namespace id
/// rebind another's. Each compilation's NamespaceContext numbers new URIs from 100 + count, so
/// two queries offer the SAME numeric id for DIFFERENT URIs; the store adopted it anyway and kept
/// the first binding, so later nodes resolved to someone else's namespace (QT3
/// K2-Serialization-40/41/42 after K2-Serialization-12 in the same run).
/// </summary>
public class SharedStoreNamespaceIdTests
{
    [Fact]
    public void A_preferred_id_already_bound_to_another_uri_is_refused()
    {
        var store = new XdmDocumentStore();
        INodeBuilder builder = store;
        var pinned = new NamespaceId(108);

        var a = builder.InternNamespace("http://www.example.com/A", pinned);
        var x = builder.InternNamespace("http://www.w3.org/XML/1998/namespace", pinned);

        a.Should().Be(pinned, "the first URI to offer a free id may adopt it");
        x.Should().NotBe(pinned, "the id is taken by a different URI");
        ((INodeStore)store).GetNamespaceUri(a).Should().Be("http://www.example.com/A");
        ((INodeStore)store).GetNamespaceUri(x).Should().Be("http://www.w3.org/XML/1998/namespace");
    }

    [Fact]
    public void Re_offering_a_uri_its_own_id_is_accepted()
    {
        var store = new XdmDocumentStore();
        INodeBuilder builder = store;
        var pinned = new NamespaceId(108);
        builder.InternNamespace("http://www.example.com/A", pinned).Should().Be(pinned);
        builder.InternNamespace("http://www.example.com/A", pinned).Should().Be(pinned);
    }

    [Fact]
    public async Task Xml_space_serializes_after_an_earlier_query_on_the_same_store()
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);

        async Task<object?> Run(string q)
        {
            var compiled = engine.Compile(q);
            compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
            var items = new List<object?>();
            await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                items.Add(item);
            return items.Count == 1 ? items[0] : items.ToArray();
        }

        var options = new SerializationOptions { Method = OutputMethod.Xml, OmitXmlDeclaration = true };

        var first = await Run("<e xmlns:a=\"http://www.example.com/A\" a:a=\"value\"/>");
        var second = await Run("<a xml:space=\"preserve\"><x/></a>");

        XQueryResultSerializer.Serialize(second, store, options)
            .Should().Contain("xml:space=\"preserve\"");
        XQueryResultSerializer.Serialize(first, store, options)
            .Should().Contain("\"http://www.example.com/A\"", "the earlier binding must survive");
    }
}
