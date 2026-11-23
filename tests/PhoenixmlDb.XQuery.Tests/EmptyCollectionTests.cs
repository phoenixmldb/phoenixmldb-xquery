using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using AwesomeAssertions;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A collection that exists and has no documents is the empty sequence; FODC0002 is for a
/// collection that does not exist (F&amp;O 3.1 §14.6.2, §14.6.3; xquery#418). The resolver says
/// which through <see cref="IDocumentResolver.TryResolveCollection"/>: ResolveCollection gives an
/// empty sequence for both, so every empty collection was FODC0002.
/// </summary>
public sealed class EmptyCollectionTests
{
    private static readonly string[] Functions = ["collection", "uri-collection"];

    private static async Task<string> Run(IDocumentResolver resolver, string query, ResourcePolicy? policy = null)
    {
        var store = resolver as XdmDocumentStore ?? new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: resolver) { ResourcePolicy = policy };
        try
        {
            var items = new List<string>();
            await foreach (var item in engine.Compile(query).ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                items.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? "");
            return string.Join(" ", items);
        }
        catch (XQueryRuntimeException e)
        {
            return "error " + e.ErrorCode;
        }
    }

    /// <summary>
    /// A resolver of a host's own, as a database has: the default collection and urn:empty
    /// exist and are empty, urn:one holds one document, and no other collection exists.
    /// </summary>
    private sealed class DatabaseResolver : IDocumentResolver
    {
        private readonly XdmDocument _one = new XdmDocumentStore().LoadFromString("<one/>", "urn:one/1.xml");

        public XdmDocument? ResolveDocument(string uri) => null;
        public bool IsDocumentAvailable(string uri) => false;
        public IEnumerable<XdmNode> ResolveCollection(string? uri) => uri == "urn:one" ? [_one] : [];

        public bool TryResolveCollection(string? uri, [NotNullWhen(true)] out IEnumerable<XdmNode>? documents)
        {
            documents = uri switch
            {
                null or "urn:empty" => [],
                "urn:one" => [_one],
                _ => null,
            };
            return documents != null;
        }
    }

    /// <summary>A resolver written before TryResolveCollection: it does not override it.</summary>
    private sealed class OlderResolver : IDocumentResolver
    {
        public XdmDocument? ResolveDocument(string uri) => null;
        public bool IsDocumentAvailable(string uri) => false;
        public IEnumerable<XdmNode> ResolveCollection(string? uri) => [];
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("uri-collection")]
    public async Task An_empty_collection_of_a_hosts_resolver_is_the_empty_sequence(string function)
    {
        var db = new DatabaseResolver();
        (await Run(db, $"count({function}())")).Should().Be("0");
        (await Run(db, $"count({function}(()))")).Should().Be("0");
        (await Run(db, $"count({function}('urn:empty'))")).Should().Be("0");
        (await Run(db, $"count({function}('urn:one'))")).Should().Be("1");
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("uri-collection")]
    public async Task A_collection_a_hosts_resolver_does_not_know_is_FODC0002(string function)
        => (await Run(new DatabaseResolver(), $"{function}('urn:none')")).Should().Be("error FODC0002");

    [Fact]
    public async Task A_resolver_that_does_not_override_the_member_answers_as_before()
    {
        foreach (var function in Functions)
        {
            (await Run(new OlderResolver(), $"{function}()")).Should().Be("error FODC0002");
            (await Run(new OlderResolver(), $"{function}('urn:empty')")).Should().Be("error FODC0002");
        }
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("uri-collection")]
    public async Task Behind_a_policy_the_hosts_resolver_still_decides(string function)
    {
        var db = new DatabaseResolver();
        (await Run(db, $"count({function}())", ResourcePolicy.ServerDefault)).Should().Be("0");
        (await Run(db, $"count({function}('urn:empty'))", ResourcePolicy.CreateBuilder().AllowScheme("urn").Build()))
            .Should().Be("0");
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("uri-collection")]
    public async Task A_registered_empty_collection_is_the_empty_sequence_with_or_without_a_policy(string function)
    {
        foreach (var policy in new[] { null, ResourcePolicy.ServerDefault })
        {
            var store = new XdmDocumentStore();
            store.LoadFromString("<loaded/>", "urn:loaded.xml");
            store.RegisterCollection("", []);
            (await Run(store, $"count({function}())", policy)).Should().Be("0", $"policy {policy != null}");
        }
    }

    public static TheoryData<string, string> RegisteredBehindAPolicy => new()
    {
        { "", "collection()" },
        { "urn:c", "collection('urn:c')" },
    };

    [Theory]
    [MemberData(nameof(RegisteredBehindAPolicy))]
    public async Task A_registered_collection_of_atomic_values_is_found_behind_a_policy(string name, string call)
    {
        // Behind a policy the functions did not find what the host registered: FODC0002, and
        // with TryResolveCollection alone the empty sequence.
        foreach (var policy in new[] { null, ResourcePolicy.Unrestricted, ResourcePolicy.CreateBuilder().AllowScheme("urn").Build() })
        {
            var store = new XdmDocumentStore();
            store.RegisterCollection(name, ["a", 1]);
            (await Run(store, $"string-join({call} ! string(), ',')", policy)).Should().Be("a,1", $"policy {policy != null}");
        }
    }

    [Fact]
    public async Task A_policy_that_does_not_allow_the_uri_still_refuses_a_registered_collection()
    {
        var store = new XdmDocumentStore();
        store.RegisterCollection("urn:c", ["a"]);
        var act = () => Run(store, "collection('urn:c')", ResourcePolicy.ServerDefault);
        await act.Should().ThrowAsync<ResourceAccessDeniedException>();
    }

    [Fact]
    public async Task A_store_with_no_documents_has_no_default_collection()
    {
        // The store's default collection is every loaded document; with none it is unknown, as
        // before (the QT3 cases that expect FODC0002 rely on it).
        foreach (var function in Functions)
        {
            (await Run(new XdmDocumentStore(), $"{function}()")).Should().Be("error FODC0002");
            var managed = new XdmDocumentStore();
            managed.LoadFromString("<loaded/>", "urn:loaded.xml");
            managed.ClearCollections(explicitOnly: true);
            (await Run(managed, $"{function}()")).Should().Be("error FODC0002");
        }
    }

    private sealed class Supplier : ResourceResolverBase
    {
        public override IEnumerable<XdmNode>? ResolveCollection(string? uri) => uri == "empty" ? [] : null;
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("uri-collection")]
    public async Task An_empty_collection_the_policys_resolver_serves_is_the_empty_sequence(string function)
    {
        // IResourceResolver: null is "not mine"; an empty sequence is a collection with nothing in it.
        var policy = ResourcePolicy.CreateBuilder().WithResourceResolver(new Supplier()).Build();
        (await Run(new XdmDocumentStore(), $"count({function}('empty'))", policy)).Should().Be("0");
    }
}
