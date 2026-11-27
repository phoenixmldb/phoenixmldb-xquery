using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// The store of a copy modify expression registers its nodes in the query's store, so it takes its
/// namespace ids from that store, as it takes its node ids (#441). Its own counter started at the
/// first user id, as the query's store does, so it could give a URI an id that the query's store
/// had for another URI, or did not know.
/// </summary>
public class CopyStoreNamespaceIdTests
{
    /// <summary>
    /// The analyzer registers the query's namespaces in the query's store from the first user id
    /// (<see cref="XdmDocumentStore.RegisterNamespace"/>); the copy store's own counter started
    /// there too, so it gave a new URI the id of a namespace of the query.
    /// </summary>
    [Fact]
    public void The_copy_store_takes_namespace_ids_from_the_query_store()
    {
        var queryStore = new XdmDocumentStore();
        var a = new NamespaceId(NamespaceId.FirstUserNamespaceId);
        queryStore.RegisterNamespace("urn:a", a);
        var copies = new InMemoryUpdatableNodeStore(queryStore);

        var b = copies.InternNamespace("urn:b");

        b.Should().NotBe(a, "the id of urn:a in the query's store");
        (queryStore.ResolveNamespaceUri(b)?.ToString()).Should().Be("urn:b", "the query's store knows the id the copy store gave");
        (queryStore.ResolveNamespaceUri(a)?.ToString()).Should().Be("urn:a");
        copies.GetNamespaceUri(b).Should().Be("urn:b");
        copies.InternNamespace("urn:a").Should().Be(a, "a URI the query's store has keeps its id");
        copies.InternNamespace("urn:b").Should().Be(b);
    }

    /// <summary>Guard: a store with no query store counts its own ids, from the first user id.</summary>
    [Fact]
    public void A_store_of_its_own_counts_its_own_namespace_ids()
    {
        var store = new InMemoryUpdatableNodeStore();
        var id = store.InternNamespace("urn:a");
        id.Value.Should().Be(NamespaceId.FirstUserNamespaceId);
        store.GetNamespaceUri(id).Should().Be("urn:a");
    }
}
