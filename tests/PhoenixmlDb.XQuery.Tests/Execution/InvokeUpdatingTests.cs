using AwesomeAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XQUF 3.0 §5.5 and §7.11. An updating function is called dynamically with
/// <c>invoke updating $f(...)</c>, an updating expression; an ordinary dynamic call of one,
/// <c>$f(...)</c>, is XUDY0038. Before, <c>invoke updating</c> did not parse, and <c>$f(...)</c>
/// applied the function's updates although it is a simple expression.
/// </summary>
public class InvokeUpdatingTests
{
    private const string Doc = "urn:test:d";
    private const string A = "doc('urn:test:d')/r/a";
    private const string Del = "declare %updating function local:del($n) { delete node $n }; ";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r><a>a</a><b>b</b></r>", Doc);
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

    private static Task<string> Names(QueryEngine engine)
        => RunAsync(engine, "string-join(doc('urn:test:d')/r/*!name(), ' ')");

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

    // Function items that are updating functions: each is called with the element a.
    public static TheoryData<string> UpdatingFunctionItems => new()
    {
        "local:del#1",
        "function-lookup(xs:QName('local:del'), 1)",
        "%updating function($n) { delete node $n }",
        // §7.10: a partial application of an updating function returns an updating function.
        "local:del(?)",
        "local:del#1(?)",
    };

    [Theory]
    [MemberData(nameof(UpdatingFunctionItems))]
    public async Task A_dynamic_call_of_an_updating_function_is_XUDY0038_and_changes_nothing(string item)
    {
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, $"{Del}let $f := {item} return $f({A})");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0038");
        (await Names(engine)).Should().Be("a b");
    }

    [Theory]
    [MemberData(nameof(UpdatingFunctionItems))]
    public async Task Invoke_updating_calls_an_updating_function(string item)
    {
        var (_, engine) = Setup();

        await RunAsync(engine, $"{Del}let $f := {item} return invoke updating $f({A})");

        (await Names(engine)).Should().Be("b");
    }

    // The other forms of a dynamic call: the arrow, a lookup, an array member.
    [Theory]
    [InlineData($"{A} => $f()")]
    [InlineData($"map {{ 'f': $f }}?f({A})")]
    [InlineData($"[$f](1)({A})")]
    public async Task Each_dynamic_call_of_an_updating_function_is_XUDY0038(string call)
    {
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, $"{Del}let $f := local:del#1 return {call}");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0038");
        (await Names(engine)).Should().Be("a b");
    }

    [Fact]
    public async Task An_updating_function_coerced_to_a_function_type_is_still_updating()
    {
        // Function coercion (XPath 3.1 §3.1.5.2) wraps the item in another function item. (XQUF 3.0
        // §2.3.2, by which a function test without %updating matches no updating function, is not
        // implemented: the coercion is accepted.)
        const string G = "declare function local:g($f as function(element()) as empty-sequence()) { $f }; ";
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, $"{Del}{G}let $h := local:g(local:del#1) return $h({A})");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0038");
        (await Names(engine)).Should().Be("a b");

        await RunAsync(engine, $"{Del}{G}let $h := local:g(local:del#1) return invoke updating $h({A})");

        (await Names(engine)).Should().Be("b");
    }

    // Operands that a later pass rewrites (a named reference resolved, a constant folded): the
    // rewritten call is still an invoke updating.
    [Theory]
    [InlineData($"invoke updating local:del#1({A})")]
    [InlineData($"let $f := %updating function($n, $i) {{ delete node $n[$i] }} return invoke updating $f({A}, 1 + 0)")]
    public async Task Invoke_updating_with_operands_that_are_rewritten(string query)
    {
        var (_, engine) = Setup();

        await RunAsync(engine, Del + query);

        (await Names(engine)).Should().Be("b");
    }

    [Fact]
    public void A_function_item_that_wraps_an_updating_function_is_updating()
    {
        // No path wraps an updating function in these today (they wrap resource, context and
        // variadic functions); each presents the function it wraps, updating included.
        var put = new PhoenixmlDb.XQuery.Functions.PutFunction(2);
        XQueryFunction[] wrappers =
        [
            new ModuleBoundFunctionRef(put, null, null),
            new ContextBoundFunctionRef(put, null),
            new VariadicFunctionRefItem(put, 2),
        ];

        wrappers.Should().AllSatisfy(w => w.IsUpdating.Should().BeTrue());
    }

    [Fact]
    public async Task A_dynamic_call_of_fn_put_is_XUDY0038()
    {
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, "let $f := put#2 return $f(<n/>, 'n.xml')");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0038");
    }

    [Fact]
    public async Task Invoke_updating_calls_fn_put()
    {
        // The example of §5.5. With no resource policy fn:put is refused when the updates are
        // applied (phx:XUDY9001): so the call reached it.
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, "let $f := fn:put#2 return invoke updating $f(<newnode/>, 'newnode.xml')");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY9001");
    }

    [Fact]
    public async Task A_partial_application_of_an_updating_function_item_is_not_a_call()
    {
        // §7.11: XUDY0038 is for a dynamic call that is not a partial function application.
        var (_, engine) = Setup();

        await RunAsync(engine, $"{Del}let $f := local:del#1 let $p := $f(?) return invoke updating $p({A})");

        (await Names(engine)).Should().Be("b");
    }

    [Theory]
    [InlineData("let $f := upper-case#1 return invoke updating $f('a')", "A")]
    [InlineData("invoke updating (function($x) { $x + 1 })(1)", "2")]
    public async Task Invoke_updating_calls_a_simple_function_and_returns_its_result(string query, string result)
    {
        var (_, engine) = Setup();

        (await RunAsync(engine, query)).Should().Be(result);
    }

    [Fact]
    public async Task The_updates_of_the_arguments_are_applied_with_those_of_the_function()
    {
        var (_, engine) = Setup();

        await RunAsync(engine,
            $"{Del}let $f := local:del#1 return invoke updating $f((doc('{Doc}')/r/b, delete node {A}))");

        (await Names(engine)).Should().Be("");
    }

    [Fact]
    public async Task Invoke_updating_is_an_updating_function_body()
    {
        var (_, engine) = Setup();

        await RunAsync(engine,
            Del + "declare %updating function local:apply($f, $n) { invoke updating $f($n) }; " +
            $"local:apply(local:del#1, {A})");

        (await Names(engine)).Should().Be("b");
    }

    [Fact]
    public async Task A_dynamic_call_of_a_simple_function_is_unchanged()
    {
        var (_, engine) = Setup();

        (await RunAsync(engine, "let $f := upper-case#1 return $f('a')")).Should().Be("A");
    }

    // §5.5: an updating expression; where 3.0 requires a simple one it is XUST0001.
    [Theory]
    [InlineData("let $f := upper-case#1, $x := invoke updating $f('a') return 1")]
    [InlineData("let $f := upper-case#1 return if (invoke updating $f('a')) then 1 else 2")]
    [InlineData("declare function local:s($f) { invoke updating $f('a') }; local:s(upper-case#1)")]
    public void Invoke_updating_where_a_simple_expression_is_required_is_XUST0001(string query)
        => StaticError(query).Should().Be("XUST0001");

    [Fact]
    public void Invoke_updating_is_not_a_partial_function_application()
        => StaticError("let $f := upper-case#1 return invoke updating $f(?)").Should().Be("XPST0003");

    [Fact]
    public async Task Invoke_is_still_a_name()
    {
        var (_, engine) = Setup();

        (await RunAsync(engine, "<r><invoke><updating/></invoke></r>/invoke/updating ! name()")).Should().Be("updating");
    }

    [Fact]
    public void Invoke_updating_is_not_XPath()
    {
        var act = () => new XQueryParserFacade { XPath = true }.Parse("invoke updating $f(1)");

        act.Should().Throw<XQueryParseException>().Which.Message.Should().Contain("an update expression");
    }
}
