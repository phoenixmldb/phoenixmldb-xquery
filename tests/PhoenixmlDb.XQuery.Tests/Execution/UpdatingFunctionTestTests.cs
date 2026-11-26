using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XQUF 3.0 §2.3.2: a function test without an updating function assertion matches only simple
/// functions; <c>%updating</c> (<c>%updating("allowed")</c>) matches both kinds,
/// <c>%updating("required")</c> only updating functions, and <c>%simple</c>
/// (<c>%updating("prohibited")</c>) only simple functions. An updating function was a
/// <c>function(*)</c>: a higher-order function took it and called it, and its updates were applied
/// although the call is a simple expression (<c>for-each($n, local:del#1)</c> deleted the nodes).
/// </summary>
public class UpdatingFunctionTestTests
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

    // An item, a function test, and whether the item matches it.
    public static TheoryData<string, string, bool> Matches => new()
    {
        // An updating function: only a test with %updating, allowed or required, matches it.
        { "local:del#1", "function(*)", false },
        { "local:del#1", "%simple function(*)", false },
        { "local:del#1", "%updating(\"prohibited\") function(*)", false },
        { "local:del#1", "%updating function(*)", true },
        { "local:del#1", "%updating(\"allowed\") function(*)", true },
        { "local:del#1", "%updating(\"required\") function(*)", true },
        { "local:del#1", "function(item()*) as item()*", false },
        { "local:del#1", "%updating function(item()*) as item()*", true },
        { "local:del#1", "(%updating function(*))", true },
        { "%updating function($n) { delete node $n }", "function(*)", false },
        { "%updating function($n) { delete node $n }", "%updating(\"required\") function(*)", true },
        { "local:del(?)", "function(*)", false },
        { "local:del(?)", "%updating(\"required\") function(*)", true },
        { "function-lookup(xs:QName('local:del'), 1)", "function(*)", false },
        { "put#2", "function(*)", false },
        { "put#2", "%updating(\"required\") function(*)", true },
        // A simple function, a map and an array: every test but %updating("required") matches.
        { "count#1", "function(*)", true },
        { "count#1", "%simple function(*)", true },
        { "count#1", "%updating function(*)", true },
        { "count#1", "%updating(\"required\") function(*)", false },
        { "function($n) { $n }", "%updating(\"required\") function(*)", false },
        { "map { 1: 2 }", "%updating function(*)", true },
        { "map { 1: 2 }", "%updating(\"required\") function(*)", false },
        { "[1]", "%updating(\"required\") function(*)", false },
        { "[1]", "function(*)", true },
    };

    [Theory]
    [MemberData(nameof(Matches))]
    public async Task A_function_test_matches_an_updating_function_only_with_an_updating_function_assertion(string item, string test, bool matches)
    {
        var (_, engine) = Setup();

        (await RunAsync(engine, $"{Del}{item} instance of {test}")).Should().Be(matches ? "True" : "False");
        (await Names(engine)).Should().Be("a b");
    }

    [Fact]
    public async Task Typeswitch_and_treat_as_follow_the_assertion()
    {
        var (_, engine) = Setup();

        (await RunAsync(engine, $"{Del}typeswitch (local:del#1) case function(*) return 'simple' "
            + "case %updating function(*) return 'updating' default return 'none'")).Should().Be("updating");
        var treat = () => RunAsync(engine, $"{Del}count(local:del#1 treat as function(*))");
        (await treat.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPDY0050");
        (await RunAsync(engine, $"{Del}count(local:del#1 treat as %updating function(*))")).Should().Be("1");
    }

    // A higher-order function given an updating function: XPTY0004 before it calls it.
    public static TheoryData<string> HigherOrderCalls => new()
    {
        $"for-each({A}, local:del#1)",
        $"for-each({A}, %updating function($n) {{ delete node $n }})",
        $"apply(local:del#1, [{A}])",
        $"apply(for-each#2, [{A}, local:del#1])",
        $"let $fe := for-each#2 return $fe({A}, local:del#1)",
        // A dynamic call and fn:apply of a built-in that calls the function itself, and a simple
        // function whose parameter is typed function(*), given the updating function by for-each.
        $"let $fp := for-each-pair#3 return $fp({A}, {A}, %updating function($x, $y) {{ delete node $x }})",
        $"apply(for-each-pair#3, [{A}, {A}, %updating function($x, $y) {{ delete node $x }}])",
        "for-each(local:del#1, function($f as function(*)) { 1 })",
        $"let $p := for-each(?, local:del#1) return $p({A})",
        $"sort({A}, (), local:del#1)",
        $"array:sort([{A}], (), local:del#1)",
        $"filter({A}, %updating function($n) {{ delete node $n, true() }})",
        $"fold-left({A}, (), %updating function($acc, $n) {{ delete node $n }})",
        $"for-each-pair({A}, {A}, %updating function($x, $y) {{ delete node $x }})",
        $"array:for-each([{A}], local:del#1)",
        $"map:for-each(map {{ 1: {A} }}, %updating function($k, $v) {{ delete node $v }})",
        "parse-json('\"\\uFFFF\"', map { 'fallback': %updating function($s) { delete node " + A + ", 'x' } })",
        "json-to-xml('\"\\uFFFF\"', map { 'fallback': %updating function($s) { delete node " + A + ", 'x' } })",
    };

    [Theory]
    [MemberData(nameof(HigherOrderCalls))]
    public async Task A_higher_order_function_given_an_updating_function_is_XPTY0004_and_nothing_changes(string call)
    {
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, $"{Del}{call}");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
        (await Names(engine)).Should().Be("a b");
    }

    // Every built-in function, called with an updating function in each argument position (the
    // other arguments the element a, or the empty sequence): the document never changes. A
    // built-in that called the updating function and returned would have applied its update from
    // a simple expression. Errors are expected (most built-ins take no function); a change is not.
    [Fact]
    public async Task No_built_in_function_applies_the_updates_of_an_updating_function_it_is_given()
    {
        var uris = new Dictionary<PhoenixmlDb.Core.NamespaceId, string>
        {
            [FunctionNamespaces.Fn] = "http://www.w3.org/2005/xpath-functions",
            [FunctionNamespaces.Map] = "http://www.w3.org/2005/xpath-functions/map",
            [FunctionNamespaces.Array] = "http://www.w3.org/2005/xpath-functions/array",
            [FunctionNamespaces.Math] = "http://www.w3.org/2005/xpath-functions/math",
        };
        var changed = new List<string>();
        var hung = new List<string>();
        var calls = 0;
        foreach (var function in FunctionLibrary.Standard.GetAllFunctions())
        {
            if (!uris.TryGetValue(function.Name.Namespace, out var uri) || function.Arity == 0
                || function.Name.LocalName is "put" or "transform" or "load-xquery-module")
                continue;
            for (var position = 0; position < function.Arity; position++)
            {
                foreach (var other in new[] { A, "()" })
                {
                    var args = Enumerable.Range(0, function.Arity).Select(i => i == position ? "local:del#1" : other);
                    var query = $"{Del}Q{{{uri}}}{function.Name.LocalName}({string.Join(", ", args)})";
                    var (_, engine) = Setup();
                    calls++;
#pragma warning disable CA1031 // any error is expected here; only a change of the document fails the test
                    var run = Task.Run(async () =>
                    {
                        try
                        {
                            var compiled = engine.Compile(query);
                            if (compiled.Success)
                                await foreach (var _ in engine.ExecuteAsync(compiled.ExecutionPlan!)) { }
                        }
                        catch (Exception)
                        {
                        }
                    });
#pragma warning restore CA1031
                    try
                    {
                        await run.WaitAsync(TimeSpan.FromSeconds(30));
                    }
                    catch (TimeoutException)
                    {
                        hung.Add(query[Del.Length..]);
                        continue;
                    }
                    if (await Names(engine) != "a b")
                        changed.Add(query[Del.Length..]);
                }
            }
        }

        calls.Should().BeGreaterThan(500);
        // Every call, not only the first, in the message.
        string.Join("\n", changed).Should().BeEmpty();
        string.Join("\n", hung).Should().BeEmpty();
    }

    [Fact]
    public async Task A_higher_order_function_still_takes_a_simple_function()
    {
        var (_, engine) = Setup();

        (await RunAsync(engine, $"for-each({A}, name#1)")).Should().Be("a");
        (await RunAsync(engine, "apply(count#1, [(1, 2)])")).Should().Be("2");
        (await RunAsync(engine, "sort((3, 1), (), function($x) { -$x })")).Should().Be("3 1");
    }

    // Coercion to a function type: a parameter, a variable, a result.
    public static TheoryData<string> CoercionsThatFail => new()
    {
        $"declare function local:g($f as function(*)) {{ 1 }}; local:g(local:del#1)",
        $"declare function local:g($f as function(node()) as empty-sequence()) {{ 1 }}; local:g(local:del#1)",
        $"declare function local:g($f as %simple function(*)) {{ 1 }}; local:g(local:del#1)",
        "declare function local:g($f as %updating(\"required\") function(*)) { 1 }; local:g(count#1)",
        "(function($f as function(*)) { 1 })(local:del#1)",
        "let $f as function(*) := local:del#1 return 1",
        "declare function local:r() as function(*) { local:del#1 }; count(local:r())",
        "declare function local:r() as function(node()) as empty-sequence() { local:del#1 }; count(local:r())",
        "count((function() as function(*) { local:del#1 })())",
    };

    [Theory]
    [MemberData(nameof(CoercionsThatFail))]
    public async Task Coercion_to_a_function_type_without_updating_is_XPTY0004(string query)
    {
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, Del + query);

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
        (await Names(engine)).Should().Be("a b");
    }

    [Fact]
    public async Task A_parameter_declared_updating_takes_an_updating_function_and_invoke_updating_calls_it()
    {
        var (_, engine) = Setup();

        (await RunAsync(engine, Del + "declare function local:g($f as %updating function(*)) { 1 }; local:g(local:del#1)")).Should().Be("1");
        (await RunAsync(engine, Del + "declare function local:g($f as %updating function(*)) { 1 }; local:g(count#1)")).Should().Be("1");
        await RunAsync(engine, Del
            + "declare %updating function local:g($f as %updating(\"required\") function(node()) as empty-sequence(), $n) { invoke updating $f($n) }; "
            + $"local:g(local:del#1, {A})");

        (await Names(engine)).Should().Be("b");
    }

    [Fact]
    public async Task A_typed_function_test_with_updating_parameters_is_contravariant()
    {
        var (_, engine) = Setup();

        // A function that takes any function matches a test whose parameter is a simple function,
        // not the reverse.
        (await RunAsync(engine, "function($f as %updating function(*)) { 1 } instance of function(function(*)) as item()*")).Should().Be("True");
        (await RunAsync(engine, "function($f as function(*)) { 1 } instance of function(%updating function(*)) as item()*")).Should().Be("False");
        (await RunAsync(engine, "function($f as function(*)) { 1 } instance of function(function(*)) as item()*")).Should().Be("True");
        // A map(K, V) parameter type against function(K) as V?: V keeps its assertion.
        (await RunAsync(engine, "function($m as function(xs:string) as %updating(\"required\") function(*)?) { 1 } "
            + "instance of function(map(xs:string, %updating(\"required\") function(*))) as item()*")).Should().Be("True");
    }

    [Fact]
    public async Task The_error_names_the_assertion_of_the_type()
    {
        var (_, engine) = Setup();

        var act = () => RunAsync(engine, "declare function local:g($f as %updating(\"required\") function(*)) { 1 }; local:g(count#1)");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.Message.Should().Contain("%updating(\"required\") function(*)");
    }

    // XUST0034: XQUF 3.0 §2.3.2, "It is a static error to use more than one updating function
    // assertion in the function assertion set of a FunctionTest". XQUF 3.0 gives no code for an
    // assertion value it does not define; XQuery 3.1 §2.5.5.7: "it is a static error
    // [err:XQST0045] for a user to define a function assertion in a reserved namespace", and
    // %updating and %simple are in the XQuery namespace.
    public static TheoryData<string, string> StaticErrors => new()
    {
        { "1 instance of %simple %updating function(*)", "XUST0034" },
        { "1 instance of %updating %updating(\"required\") function(*)", "XUST0034" },
        { "1 instance of %updating(\"maybe\") function(*)", "XQST0045" },
        { "1 instance of %updating(\"required\", \"allowed\") function(*)", "XQST0045" },
        { "1 instance of %simple(\"prohibited\") function(*)", "XQST0045" },
        { "1 instance of %updating(1) function(*)", "XQST0045" },
    };

    [Theory]
    [MemberData(nameof(StaticErrors))]
    public void More_than_one_assertion_or_an_unknown_one_is_a_static_error(string query, string code)
        => StaticError(query).Should().Be(code);

    // XQuery 3.1 §2.5.5.7: a function assertion in a namespace the processor does not recognise is
    // ignored.
    [Fact]
    public void An_assertion_with_another_annotation_and_the_xq_prefix_compiles()
        => StaticError("declare namespace x = 'urn:x'; declare namespace xq = 'http://www.w3.org/2012/xquery'; "
            + "1 instance of %x:note %xq:updating(\"required\") function(*)").Should().BeNull();
}
