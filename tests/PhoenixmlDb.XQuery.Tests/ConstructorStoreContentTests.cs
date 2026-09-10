using FluentAssertions;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A direct element constructor whose content is a node drawn from a node store returns an
/// xs:string of its own serialized markup instead of an element node, so any path step applied
/// to it fails:
///
///   count(&lt;w&gt;{$c/region}&lt;/w&gt;/*)
///     -> axis step (Child::*) used when the context item is not a node
///        (got xs:string "&lt;w&gt;northeast&lt;/w&gt;")
///
/// Reported by the phoenixml engine repo against an LMDB container, but it is NOT specific to
/// their node provider — they reproduced it on XQuery's own <see cref="XdmDocumentStore"/> too,
/// which is what these tests drive. Literal content with no store involved works, which is what
/// isolates it to store-sourced content.
/// </summary>
public sealed class ConstructorStoreContentTests
{
    private static async Task<List<object?>> RunAsync(XdmDocumentStore store, string query)
    {
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compilation = engine.Compile(query);
        compilation.Success.Should().BeTrue(string.Join("; ", compilation.Errors.Select(e => e.Message)));
        var context = engine.CreateContext();
        var results = new List<object?>();
        await foreach (var item in compilation.ExecutionPlan!.ExecuteAsync(context))
            results.Add(item);
        return results;
    }

    private static XdmDocumentStore StoreWithCustomers()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<doc><c><region>west</region></c><c><region>east</region></c></doc>", "cust");
        return store;
    }

    [Fact]
    public async Task Constructor_WithLiteralContent_YieldsElement()
    {
        // Control: no store content anywhere in the constructor.
        var results = await RunAsync(StoreWithCustomers(), "count((<w><r>west</r></w>)/*)");

        results.Should().ContainSingle().Which.Should().Be(1L);
    }

    [Fact]
    public async Task Constructor_WithStoreSourcedContent_YieldsElementNotString()
    {
        var results = await RunAsync(StoreWithCustomers(),
            "count(for $c in doc('cust')/doc/c return <w>{$c/region}</w>/*)");

        results.Should().ContainSingle().Which.Should().Be(2L,
            "the constructor must yield an element node whose child is the copied region, "
            + "not an xs:string of its own markup");
    }

    [Fact]
    public async Task Constructor_WithStoreSourcedContent_IsANode()
    {
        var results = await RunAsync(StoreWithCustomers(),
            "for $c in doc('cust')/doc/c return <w>{$c/region}</w> instance of element()");

        results.Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public async Task Constructor_Parenthesised_WithStoreSourcedContent_YieldsElement()
    {
        // The engine repo saw the parenthesised form fail too, ruling out a parse-level
        // association of the path step with the constructor's content.
        var results = await RunAsync(StoreWithCustomers(),
            "count((<w>{doc('cust')/doc/c[1]/region}</w>)/*)");

        results.Should().ContainSingle().Which.Should().Be(1L);
    }
}
