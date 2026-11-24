using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A copy modify expression collects the updates of its modify clause in a list of its own and
/// applies them to the copies before the return clause runs (XQUF 3.0 §5.6). That list stayed the
/// context's list until the expression ended, so while the return clause yielded its items the
/// context still held the spent modify list: a host that checks the list before each item (a
/// database refuses a query that changes stored documents) saw updates the query had not made,
/// and an update in the return clause went into a list that had already been applied, and was
/// lost. The expression's own pending update list is the one active outside it.
/// </summary>
public class CopyModifyPendingUpdateTests
{
    private const string Doc = "urn:test:d";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup(string xml)
    {
        var store = new XdmDocumentStore();
        store.LoadFromString(xml, Doc);
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

    public static TheoryData<string, string> TopLevel => new()
    {
        { "copy $c := <x><y/></x> modify insert node <z/> into $c return serialize($c)", "<x><y/><z/></x>" },
        { "copy $c := doc('urn:test:d') modify insert node <z/> into $c/r return (count($c//a), count($c//z))", "1 1" },
        { "copy $a := <a/>, $b := <b/> modify (insert node <x/> into $a, rename node $b as 'c') return (name($b), count($a/x))", "c 1" },
        { "for $i in 1 to 2 return copy $c := <x/> modify insert node <z/> into $c return count($c/z)", "1 1" },
    };

    // What a database checks before it hands out each item of a query's result: the query has
    // made no update. The modify clause's updates are applied to the copies, and are not the query's.
    [Theory]
    [MemberData(nameof(TopLevel))]
    public async Task While_the_return_clause_yields_the_list_outside_is_active(string query, string expected)
    {
        var (_, engine) = Setup("<r><a/></r>");
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var context = engine.CreateContext();

        var items = new List<string?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(context))
        {
            context.PendingUpdates.HasUpdates.Should().BeFalse(
                $"item {items.Count + 1} is handed out with the query's own list active, and the query made no update");
            items.Add(item?.ToString());
        }

        string.Join(" ", items).Should().Be(expected);
        (await RunAsync(engine, "count(doc('urn:test:d')//z)")).Should().Be("0", "the modify clause changed a copy");
    }

    [Fact]
    public async Task An_update_in_the_return_clause_is_applied_when_the_query_ends()
    {
        var (_, engine) = Setup("<r><a/></r>");

        (await RunAsync(engine, "copy $c := <x/> modify insert node <z/> into $c return insert node <w/> into doc('urn:test:d')/r"))
            .Should().BeEmpty();

        (await RunAsync(engine, "string-join(doc('urn:test:d')/r/*!name(), ' ')")).Should().Be("a w",
            "the return clause's update belongs to the query's list (XQUF 3.0 §5.6), not to the modify clause's, which was already applied");
    }

    // The content of an update in the return clause is copied into the query's store, where it
    // is when the query's list is applied. It was copied into the copies' store, which had gone by
    // then: the copy was placed without its children.
    public static TheoryData<string, string> ReturnClauseContent => new()
    {
        { "copy $c := <x/> modify insert node <z/> into $c return insert node $c into doc('urn:test:d')/r", "<r><a/><x><z/></x></r>" },
        { "copy $c := <x/> modify insert node <z/> into $c return insert node <q>{$c}</q> into doc('urn:test:d')/r", "<r><a/><q><x><z/></x></q></r>" },
        { "copy $c := <x/> modify insert node <z/> into $c return replace node doc('urn:test:d')/r/a with $c", "<r><x><z/></x></r>" },
    };

    [Theory]
    [MemberData(nameof(ReturnClauseContent))]
    public async Task An_update_in_the_return_clause_places_the_modified_copy(string query, string expected)
    {
        var (_, engine) = Setup("<r><a/></r>");

        (await RunAsync(engine, query)).Should().BeEmpty();

        (await RunAsync(engine, "serialize(doc('urn:test:d')/r)")).Should().Be(expected);
    }

    // A copy modify expression in a modify clause: its return clause updates what that modify
    // clause updates, the outer copies, with the outer list.
    [Fact]
    public async Task A_return_clause_inside_a_modify_clause_updates_the_outer_copy()
    {
        var (_, engine) = Setup("<r><a/></r>");

        (await RunAsync(engine,
                "copy $o := <o/> modify (copy $i := <i/> modify insert node <z/> into $i return insert node $i into $o) return serialize($o)"))
            .Should().Be("<o><i><z/></i></o>");
    }

    // The copy clause's operands may be updating expressions too (XQUF 3.0 §5.6); their updates
    // are the query's. They went into the modify clause's list and were refused, as changes of
    // nodes the copy clause did not make.
    [Fact]
    public async Task An_update_in_the_copy_clause_is_applied_when_the_query_ends()
    {
        var (_, engine) = Setup("<r><a/></r>");

        (await RunAsync(engine,
                "copy $c := (insert node <w>{<v/>}</w> into doc('urn:test:d')/r, <x/>) modify insert node <z/> into $c return serialize($c)"))
            .Should().Be("<x><z/></x>");

        (await RunAsync(engine, "serialize(doc('urn:test:d')/r)")).Should().Be("<r><a/><w><v/></w></r>");
    }
}
