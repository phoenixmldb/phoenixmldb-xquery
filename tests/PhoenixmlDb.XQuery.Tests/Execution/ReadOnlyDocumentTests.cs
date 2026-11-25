using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A document a host marks read-only (<see cref="XdmDocumentStore.MarkReadOnly"/>): an update of a
/// node of it is phx:XUDY9001, and nothing changes. A database loads a stored document into an
/// <see cref="XdmDocumentStore"/> to run a query on it; since the store can be updated (#416), an
/// update changed that copy in memory and the query reported success, with the database unchanged.
/// Nodes the query constructs, and the copies of a copy clause, can still be updated.
/// </summary>
public class ReadOnlyDocumentTests
{
    private const string Stored = "urn:test:stored";
    private const string Other = "urn:test:other";
    private const string PhxNamespace = "https://schemas.phoenixml.dev/2026/functions";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup()
    {
        var store = new XdmDocumentStore();
        store.MarkReadOnly(store.LoadFromString("<r><a>a</a><b>b</b></r>", Stored));
        store.LoadFromString("<r><a>a</a><b>b</b></r>", Other);
        return (store, new QueryEngine(nodeProvider: store, documentResolver: store));
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

    private static Task<string> Names(QueryEngine engine, string uri)
        => RunAsync(engine, $"string-join(doc('{uri}')/r/*!name(), ' ') || '/' || string(doc('{uri}'))");

    public static TheoryData<string> Updates => new()
    {
        $"insert node <x/> into doc('{Stored}')/r",
        $"delete node doc('{Stored}')/r/a",
        $"replace node doc('{Stored}')/r/a with <y/>",
        $"replace value of node doc('{Stored}')/r/a with 'new'",
        $"rename node doc('{Stored}')/r/a as 'z'",
        // Every target is checked before any change: a node the query built, a node of a document
        // that can be updated, and one of the read-only document.
        $"let $c := <c/> return (insert node <n/> into $c, delete node doc('{Other}')/r/b, delete node doc('{Stored}')/r/a)",
    };

    [Theory]
    [MemberData(nameof(Updates))]
    public async Task AnUpdateOfAReadOnlyDocumentIsXUDY9001AndNothingChanges(string update)
    {
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, update);

        var error = (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which;
        error.ErrorCode.Should().Be("XUDY9001");
        error.ErrorNamespaceUri.Should().Be(PhxNamespace);
        error.Message.Should().Contain(Stored);
        (await Names(engine, Stored)).Should().Be("a b/ab");
        (await Names(engine, Other)).Should().Be("a b/ab", "nothing changes when one target cannot be updated");
    }

    [Fact]
    public async Task UpdatesInsideCopyModifyChangeTheCopy()
    {
        // The updates of a modify clause apply to the copies; the copies are not the read-only
        // document's nodes, also in the same store. (Made by an updating function: below.)
        var (_, engine) = Setup();

        var result = await RunAsync(engine, $$"""
            copy $c := doc('{{Stored}}')/r
            modify (rename node $c/a as 'z', insert node <x/> into $c, replace value of node $c/b with 'B')
            return string-join($c/*!name(), ' ') || '/' || string($c)
            """);

        result.Should().Be("z b x/aB");
        (await Names(engine, Stored)).Should().Be("a b/ab", "the copy changed, not the document");
    }

    // An updating function declared with %updating, or with the XQuery Update 1.0 keyword.
    public static TheoryData<string> UpdatingFunctions => new()
    {
        "declare %updating function local:edit($e) { rename node $e/a as 'z', insert node <x/> into $e };",
        "declare updating function local:edit($e) { rename node $e/a as 'z', insert node <x/> into $e };",
    };

    [Theory]
    [MemberData(nameof(UpdatingFunctions))]
    public async Task AnUpdatingFunctionInAModifyClauseChangesTheCopy(string declaration)
    {
        var (_, engine) = Setup();

        var result = await RunAsync(engine, declaration + $$"""
            copy $c := doc('{{Stored}}')/r
            modify local:edit($c)
            return string-join($c/*!name(), ' ') || '/' || string($c)
            """);

        result.Should().Be("z b x/ab");
        (await Names(engine, Stored)).Should().Be("a b/ab", "the copy changed, not the document");
    }

    [Theory]
    [MemberData(nameof(UpdatingFunctions))]
    public async Task AnUpdatingFunctionOnAReadOnlyDocumentIsXUDY9001AndNothingChanges(string declaration)
    {
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, declaration + $"local:edit(doc('{Stored}')/r)");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY9001");
        (await Names(engine, Stored)).Should().Be("a b/ab");
    }

    [Fact]
    public async Task ADocumentThatIsNotMarkedIsUpdatedInTheSameStore()
    {
        // The check is per target, not per store.
        var (_, engine) = Setup();

        (await RunAsync(engine, $"rename node doc('{Other}')/r/a as 'z'")).Should().BeEmpty();

        (await Names(engine, Other)).Should().Be("z b/ab");
        (await Names(engine, Stored)).Should().Be("a b/ab");
    }

    [Fact]
    public void OnlyADocumentOfTheStoreCanBeMarked()
    {
        var store = new XdmDocumentStore();
        var elsewhere = new XdmDocumentStore().LoadFromString("<r/>");

        var act = () => store.MarkReadOnly(elsewhere);

        act.Should().Throw<ArgumentException>();
    }
}
