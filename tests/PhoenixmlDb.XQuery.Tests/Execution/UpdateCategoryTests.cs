using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XQuery Update Facility 3.0 expression categories (§2.2, §4.2-4.4, §5, §7) and updating function
/// declarations (§2.3.1). An updating expression where the rules require a simple one is XUST0001;
/// none was reported, so an update in a let clause, a condition or a variable's value was
/// evaluated or dropped silently. `declare updating function` (XQuery Update 1.0) did not parse.
/// </summary>
public class UpdateCategoryTests
{
    private const string Doc = "urn:test:d";
    private const string A = "doc('urn:test:d')/r/a";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r><a>a</a><b>b</b></r>", Doc);
        return (store, new QueryEngine(nodeProvider: store, documentResolver: store));
    }

    // The code of the static error, or null when the query compiles.
    private static string? StaticError(string query)
    {
        var (_, engine) = Setup();
        try
        {
            var compiled = engine.Compile(query);
            return compiled.Success ? null : compiled.Errors[0].Code;
        }
        catch (XQueryParseException e)
        {
            return e.ErrorCode;
        }
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

    // Where 3.0 requires a simple expression (each operand below is updating).
    [Theory]
    [InlineData($"let $x := delete node {A} return 1")]
    [InlineData($"for $x in delete node {A} return 1")]
    [InlineData($"for $x in 1 where delete node {A} return 1")]
    [InlineData($"for $x in 1 order by (delete node {A}) return 1")]
    [InlineData($"for $x in 1 group by $g := (delete node {A}) return 1")]
    [InlineData($"for tumbling window $w in (1, 2) start when (delete node {A}) return 1")]
    [InlineData($"if (delete node {A}) then 1 else 2")]
    [InlineData($"typeswitch (delete node {A}) case xs:integer return 1 default return 2")]
    [InlineData($"switch (delete node {A}) case 1 return 1 default return 2")]
    [InlineData($"switch (1) case (delete node {A}) return 1 default return 2")]
    [InlineData($"declare variable $v := delete node {A}; $v")]
    [InlineData($"declare context item := delete node {A}; 1")]
    [InlineData($"declare function local:f() {{ delete node {A} }}; local:f()")]
    [InlineData($"let $f := %simple function() {{ delete node {A} }} return 1")]
    [InlineData($"true() and (delete node {A})")]
    [InlineData($"(delete node {A}) = 1")]
    [InlineData($"doc('urn:test:d')/r/(delete node a)")]
    [InlineData($"declare %updating function local:u() {{ delete node {A} }}; local:u() and true()")]
    [InlineData($"declare %updating function local:u($e) {{ delete node $e }}; let $v := {A} => local:u() return 1")]
    [InlineData($"declare updating function local:u() {{ delete node {A} }}; for $x in local:u() return 1")]
    // XQuery 4.0's thin arrow maps over its left operand like `!`, an other expression (§7.13).
    [InlineData("declare %updating function local:u() { () }; (1, 2) -> local:u()")]
    public void An_updating_expression_where_a_simple_one_is_required_is_XUST0001(string query)
        => StaticError(query).Should().Be("XUST0001");

    // Where 3.0 allows an updating expression (XQuery Update 1.0 did not, for most of these): the
    // query compiles, gives its result, and its updates are applied.
    [Theory]
    [InlineData($"(delete node {A}, 1)", "1", "b")]
    [InlineData($"if (true()) then delete node {A} else 'no'", "", "b")]
    [InlineData($"typeswitch (1) case xs:integer return delete node {A} default return 'no'", "", "b")]
    [InlineData($"switch (1) case 1 return delete node {A} default return 'no'", "", "b")]
    [InlineData($"try {{ delete node {A} }} catch * {{ 'no' }}", "", "b")]
    [InlineData($"count(<x>{{ delete node {A} }}</x>)", "1", "b")]
    [InlineData($"declare function local:f($x) {{ count($x) }}; local:f(delete node {A})", "0", "b")]
    [InlineData($"copy $c := doc('urn:test:d')/r modify 1 return count($c/*)", "2", "a b")]
    [InlineData($"declare updating function local:u($e) {{ delete node $e }}; local:u({A})", "", "b")]
    [InlineData($"declare %updating function local:u($e) {{ delete node $e }}; local:u({A})", "", "b")]
    // A parameter named by an EQName is resolved by building the declaration again; it stays an
    // updating function.
    [InlineData($"declare %updating function local:u($Q{{urn:x}}e) {{ delete node $Q{{urn:x}}e }}; local:u({A})", "", "b")]
    [InlineData($"declare %updating function local:u($e) as xs:integer {{ delete node $e, 1 }}; local:u({A})", "1", "b")]
    [InlineData($"declare %updating function local:u($e) {{ 12 }}; local:u({A})", "12", "a b")]
    [InlineData($"declare %updating function local:u($e) {{ delete node $e }}; let $f := local:u#1 return 1", "1", "a b")]
    [InlineData($"declare %updating function local:u($e) {{ delete node $e }}; let $f := local:u(?) return 1", "1", "a b")]
    [InlineData($"let $f := %updating function($e) {{ delete node $e }} return 1", "1", "a b")]
    public async Task Where_3_0_allows_an_updating_expression_the_query_runs_and_updates(string query, string result, string names)
    {
        var (_, engine) = Setup();
        (await RunAsync(engine, query)).Should().Be(result);
        (await RunAsync(engine, "string-join(doc('urn:test:d')/r/*!name(), ' ')")).Should().Be(names);
    }

    [Theory]
    [InlineData("declare %updating variable $v := 1; $v", "XUST0032")]
    [InlineData("declare %simple variable $v := 1; $v", "XUST0032")]
    [InlineData("declare updating variable $v := 1; $v", "XUST0032")]
    [InlineData("declare %updating %simple function local:f() { () }; 1", "XUST0033")]
    [InlineData("declare updating %updating function local:f() { () }; 1", "XUST0033")]
    [InlineData("let $f := %updating %simple function() { () } return 1", "XUST0033")]
    public void The_updating_annotations_are_checked(string query, string code)
        => StaticError(query).Should().Be(code);

    [Theory]
    [InlineData("local-name(<updating/>)", "updating")]
    [InlineData("declare function local:updating() { 'u' }; local:updating()", "u")]
    [InlineData("let $updating := 2 return $updating", "2")]
    public async Task Updating_is_still_a_name(string query, string result)
    {
        var (_, engine) = Setup();
        (await RunAsync(engine, query)).Should().Be(result);
    }
}
