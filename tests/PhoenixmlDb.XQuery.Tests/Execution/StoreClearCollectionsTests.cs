using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XdmDocumentStore.ClearCollections lets a host reuse one store across evaluations whose
/// collections must not carry over; explicitOnly keeps unregistered collections absent.
/// </summary>
public class StoreClearCollectionsTests
{
    private static async Task<string> Eval(XdmDocumentStore store, string query)
    {
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine(nodeProvider: store, documentResolver: store);
        var items = new List<object?>();
        try
        {
            await foreach (var i in engine.Compile(query).ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                items.Add(i);
        }
        catch (PhoenixmlDb.XQuery.Execution.XQueryRuntimeException ex)
        {
            return ex.ErrorCode;
        }
        return string.Join(",", items);
    }

    [Fact]
    public async Task Explicit_only_clears_the_default_collection()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<a/>", "urn:a");
        store.RegisterCollection("", [1, 2]);
        (await Eval(store, "count(collection())")).Should().Be("2");

        store.ClearCollections(explicitOnly: true);
        (await Eval(store, "count(collection())")).Should().Be("FODC0002");
    }

    [Fact]
    public async Task Clearing_returns_to_the_default_rules()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<a/>", "urn:a");
        store.RegisterCollection("urn:c", [1]);
        store.ClearCollections();
        (await Eval(store, "count(collection())")).Should().Be("1"); // every loaded document
    }
}
