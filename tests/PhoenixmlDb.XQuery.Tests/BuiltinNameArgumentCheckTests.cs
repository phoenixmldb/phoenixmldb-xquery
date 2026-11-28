using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A function registered under the name and arity of a built-in (a host's wrapper that applies a
/// policy, or the XSLT engine's own version) converts its arguments with the built-in's parameter
/// types, as a call of the built-in does: the item-type check, the atomization of an array (and
/// FOTY0013 for a map or a function) and the cardinality. The check of xquery#443 ran only for the
/// instances of <see cref="FunctionLibrary.Standard"/>, so a wrapper skipped it:
/// <c>parse-xml-fragment(['&lt;a/&gt;'])</c> through a wrapper parsed the text
/// "System.Collections.Generic.List`1[System.Object]", and <c>unparsed-text(map { })</c> gave no
/// FOTY0013. And a node is never a map, an array or a function: a node given to such a parameter
/// is XPTY0004 (<c>transform(&lt;options/&gt;)</c> gave FOXT0001).
/// </summary>
public class BuiltinNameArgumentCheckTests
{
    /// <summary>
    /// A host's function under a built-in's name and arity that calls the built-in. With
    /// <paramref name="loose"/> it declares every parameter <c>item()*</c>: the built-in's types
    /// must apply all the same.
    /// </summary>
    private sealed class Wrapper(XQueryFunction inner, bool loose) : XQueryFunction
    {
        public override QName Name => inner.Name;

        public override XdmSequenceType ReturnType => inner.ReturnType;

        public override IReadOnlyList<FunctionParameterDef> Parameters => loose
            ? [.. inner.Parameters.Select(p => new FunctionParameterDef { Name = p.Name, Type = XdmSequenceType.ZeroOrMoreItems })]
            : inner.Parameters;

        public override bool IsVariadic => inner.IsVariadic;

        public override int MinArity => inner.MinArity;

        public override int MaxArity => inner.MaxArity;

        public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, PhoenixmlDb.XQuery.Ast.ExecutionContext context)
            => inner.InvokeAsync(arguments, context);
    }

    // Every built-in replaced by a wrapper: a library as a host builds it (FunctionLibrary.Standard.Copy()
    // and Register).
    private static FunctionLibrary Wrapped(bool loose)
    {
        var library = FunctionLibrary.Standard.Copy();
        foreach (var function in FunctionLibrary.Standard.GetAllFunctions())
            library.Register(new Wrapper(function, loose));
        return library;
    }

    private static readonly FunctionLibrary s_same = Wrapped(loose: false);
    private static readonly FunctionLibrary s_loose = Wrapped(loose: true);

    private static async Task<string> RunAsync(FunctionLibrary functions, string query)
    {
        try
        {
            var store = new XdmDocumentStore();
            var engine = new QueryEngine(nodeProvider: store, functions: functions, documentResolver: store);
            var compiled = engine.Compile(query);
            if (!compiled.Success)
                return "ERR " + compiled.Errors[0].Code;
            var items = new List<string>();
            await foreach (var item in engine.ExecuteAsync(compiled.ExecutionPlan!))
                items.Add(item?.ToString() ?? "");
            return string.Join(" ", items);
        }
        catch (XQueryRuntimeException e)
        {
            return "ERR " + e.ErrorCode;
        }
        catch (PhoenixmlDb.XQuery.Functions.XQueryException e)
        {
            return "ERR " + e.ErrorCode;
        }
    }

    /// <summary>
    /// The calls of xquery#443's sweep (one wrong-type value for each declared parameter of each
    /// built-in): through a wrapper, each is XPTY0004 as the built-in's call is.
    /// </summary>
    [Theory]
    [MemberData(nameof(BuiltinArgumentItemTypeTests.Calls), MemberType = typeof(BuiltinArgumentItemTypeTests))]
    public async Task Through_a_wrapper_a_value_of_another_type_is_XPTY0004(string parameter, string call)
    {
        (await RunAsync(s_same, call)).Should().Be("ERR XPTY0004", "{0}: {1}", parameter, call);
        (await RunAsync(s_loose, call)).Should().Be("ERR XPTY0004", "{0}: {1} (a wrapper that declares item()*)", parameter, call);
    }

    /// <summary>The names db-engine wraps (its resource policy), with the built-in's result for each.</summary>
    public static TheoryData<string, string> WrappedNames => new()
    {
        { "count(parse-xml-fragment(['<a/>'])/a)", "1" },
        { "name(parse-xml(['<a/>'])/*)", "a" },
        { "parse-xml(map { })", "ERR FOTY0013" },
        { "parse-xml-fragment(map { })", "ERR FOTY0013" },
        { "parse-xml(1)", "ERR XPTY0004" },
        { "unparsed-text(map { })", "ERR FOTY0013" },
        { "unparsed-text(1)", "ERR XPTY0004" },
        { "unparsed-text('urn:x', 1)", "ERR XPTY0004" },
        { "unparsed-text-lines(map { })", "ERR FOTY0013" },
        { "unparsed-text-lines(1)", "ERR XPTY0004" },
        { "unparsed-text-available(map { })", "ERR FOTY0013" },
        { "unparsed-text-available(1)", "ERR XPTY0004" },
        { "json-doc(map { })", "ERR FOTY0013" },
        { "json-doc(1)", "ERR XPTY0004" },
        { "json-doc('urn:x', 'notamap')", "ERR XPTY0004" },
        { "transform('x')", "ERR XPTY0004" },
        { "transform(<options/>)", "ERR XPTY0004" },
        { "load-xquery-module(map { })", "ERR FOTY0013" },
        { "load-xquery-module(1)", "ERR XPTY0004" },
        { "load-xquery-module('urn:x', 'notamap')", "ERR XPTY0004" },
        { "parse-json('{}', 'notamap')", "ERR XPTY0004" },
    };

    [Theory]
    [MemberData(nameof(WrappedNames))]
    public async Task A_wrapped_name_gives_the_built_in_s_result(string query, string expected)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be(expected, "the built-in");
        (await RunAsync(s_same, query)).Should().Be(expected, "a wrapper with the built-in's parameters");
        (await RunAsync(s_loose, query)).Should().Be(expected, "a wrapper that declares item()*");
    }

    /// <summary>The same calls made dynamically and by partial application: the wrapper gives what the built-in gives.</summary>
    [Theory]
    [MemberData(nameof(WrappedNames))]
    public async Task A_wrapped_name_called_dynamically_gives_the_built_in_s_result(string query, string expected)
    {
        _ = expected;
        var open = query.IndexOf('(', StringComparison.Ordinal);
        var name = query[..open];
        var call = query[open..];
        if (name.StartsWith("count", StringComparison.Ordinal) || name.StartsWith("name", StringComparison.Ordinal))
            return; // the call is inside another; the rows below cover a direct call of these names
        var arity = call.Count(c => c == ',') + 1;
        foreach (var form in new[]
                 {
                     $"let $f := {name}#{arity} return $f{call}",
                     $"let $f := function-lookup(xs:QName('fn:{name}'), {arity}) return $f{call}",
                 })
        {
            var builtIn = await RunAsync(FunctionLibrary.Standard, form);
            (await RunAsync(s_same, form)).Should().Be(builtIn, form);
            (await RunAsync(s_loose, form)).Should().Be(builtIn, form + " (a wrapper that declares item()*)");
        }
    }

    /// <summary>
    /// A node given to a parameter whose type is a map, an array or a function: XPTY0004, on the static
    /// and on the dynamic path. Every such parameter of the library, with valid values elsewhere.
    /// </summary>
    public static TheoryData<string, string> NodeCalls()
    {
        var data = new TheoryData<string, string>();
        foreach (var (parameter, call) in BuiltinArgumentItemTypeTests.CallsWith(t => t is ItemType.Map or ItemType.Array or ItemType.Function ? "<a/>" : null))
        {
            data.Add(parameter, call);
        }
        return data;
    }

    [Fact]
    public void Every_map_array_and_function_parameter_is_in_the_node_sweep()
        => NodeCalls().Count.Should().BeGreaterThan(60, "the library has that many map, array and function parameters");

    [Theory]
    [MemberData(nameof(NodeCalls))]
    public async Task A_node_given_to_a_map_array_or_function_parameter_is_XPTY0004(string parameter, string call)
    {
        (await RunAsync(FunctionLibrary.Standard, call)).Should().Be("ERR XPTY0004", "{0}: {1}", parameter, call);
        (await RunAsync(s_loose, call)).Should().Be("ERR XPTY0004", "{0}: {1} (a wrapper that declares item()*)", parameter, call);
    }

    [Theory]
    [InlineData("let $f := transform#1 return $f(<options/>)")]
    [InlineData("let $f := map:size#1 return $f(<a/>)")]
    [InlineData("let $f := array:size#1 return $f(<a/>)")]
    [InlineData("let $f := for-each#2 return $f(1, <a/>)")]
    [InlineData("let $f := apply#2 return $f(<a/>, [])")]
    [InlineData("let $f := map:get#2 return $f(<a/>, 'k')")]
    [InlineData("let $f := array:get(?, 1) return $f(<a/>)")]
    public async Task A_node_given_to_a_map_array_or_function_parameter_dynamically_is_XPTY0004(string query)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be("ERR XPTY0004");
        (await RunAsync(s_loose, query)).Should().Be("ERR XPTY0004", "a wrapper that declares item()*");
    }

    // Atomization still applies for an atomic parameter: a node is its typed value.
    [Theory]
    [InlineData("abs(<n>-3</n>)", "3")]
    [InlineData("let $f := abs#1 return $f(<n>-3</n>)", "3")]
    [InlineData("string-length(<a>xy</a>)", "2")]
    [InlineData("substring(<a>abc</a>, <b>2</b>)", "bc")]
    [InlineData("let $f := substring#2 return $f(<a>abc</a>, <b>2</b>)", "bc")]
    [InlineData("count(parse-xml(<a>&lt;b/&gt;</a>)/b)", "1")]
    [InlineData("array:get([5, 6], <a>2</a>)", "6")]
    public async Task A_node_given_to_an_atomic_parameter_is_atomized(string query, string expected)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be(expected);
        (await RunAsync(s_same, query)).Should().Be(expected, "a wrapper with the built-in's parameters");
        (await RunAsync(s_loose, query)).Should().Be(expected, "a wrapper that declares item()*");
    }

    /// <summary>
    /// The static call, the dynamic call and the partial application convert the arguments of a
    /// built-in with one helper: an array in an atomic argument is its members atomized, a map is
    /// FOTY0013. The dynamic call checked only the item type: <c>$f(['ab'])</c> for
    /// <c>string-length#1</c> was XPTY0004 and a map XPTY0004.
    /// </summary>
    [Theory]
    [InlineData("string-length(['ab'])", "2")]
    [InlineData("count(parse-xml-fragment(['<a/>'])/a)", "1")]
    [InlineData("string-length(map { })", "ERR FOTY0013")]
    [InlineData("abs([-1])", "1")]
    [InlineData("let $f := string-length#1 return $f(['ab'])", "2")]
    [InlineData("let $f := parse-xml-fragment#1 return count($f(['<a/>'])/a)", "1")]
    [InlineData("let $f := string-length#1 return $f(map { })", "ERR FOTY0013")]
    [InlineData("let $f := abs#1 return $f([-1])", "1")]
    [InlineData("let $f := function-lookup(xs:QName('fn:string-length'), 1) return $f(['ab'])", "2")]
    [InlineData("let $f := string-length(?) return $f(['ab'])", "2")]
    [InlineData("let $f := parse-xml-fragment(?) return count($f(['<a/>'])/a)", "1")]
    [InlineData("let $f := string-length(?) return $f(map { })", "ERR FOTY0013")]
    [InlineData("let $f := abs(?) return $f([-1])", "1")]
    [InlineData("let $f := substring(?, [2]) return $f('abc')", "bc")]
    public async Task The_static_and_the_dynamic_call_convert_arguments_alike(string query, string expected)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be(expected);
        (await RunAsync(s_loose, query)).Should().Be(expected, "a wrapper that declares item()*");
    }

    /// <summary>
    /// The exception types do not change: a static call's cardinality and FOTY0013 errors are the
    /// context's <see cref="PhoenixmlDb.XQuery.Functions.XQueryException"/>; a dynamic call's and a
    /// partial application's conversion errors are <see cref="XQueryRuntimeException"/>, as in 3.2.0.
    /// </summary>
    [Theory]
    [InlineData("string-length(('a', 'b'))", "Functions.XQueryException XPTY0004")]
    [InlineData("string-length(map { })", "Functions.XQueryException FOTY0013")]
    [InlineData("unparsed-text(1)", "Execution.XQueryRuntimeException XPTY0004")]
    [InlineData("let $f := string-length#1 return $f(('a', 'b'))", "Execution.XQueryRuntimeException XPTY0004")]
    [InlineData("let $f := string-length#1 return $f(map { })", "Execution.XQueryRuntimeException FOTY0013")]
    [InlineData("let $f := xs:NMTOKENS#1 return $f(('a', 'b'))", "Execution.XQueryRuntimeException XPTY0004")]
    [InlineData("let $f := string-length(?) return $f(('a', 'b'))", "Execution.XQueryRuntimeException XPTY0004")]
    [InlineData("let $f := string-length(?) return $f(map { })", "Execution.XQueryRuntimeException FOTY0013")]
    public async Task The_exception_type_of_each_path_is_kept(string query, string expected)
    {
        var act = () => new XQueryFacade().EvaluateAsync(query, "<x/>");

        var error = (await act.Should().ThrowAsync<Exception>()).Which;
        var code = error switch { XQueryRuntimeException r => r.ErrorCode, PhoenixmlDb.XQuery.Functions.XQueryException q => q.ErrorCode, _ => "?" };
        $"{error.GetType().FullName!.Replace("PhoenixmlDb.XQuery.", "", StringComparison.Ordinal)} {code}".Should().Be(expected);
    }

    /// <summary>
    /// fn:concat is variadic: each argument after the declared two has the type of the last one
    /// (xs:anyAtomicType?), so a map is FOTY0013 at any position, an array is atomized, and more than
    /// one atomic value is XPTY0004 (F&amp;O 3.1 §5.4.1; the 4.0 form, which takes sequences, is not
    /// implemented).
    /// </summary>
    [Theory]
    [InlineData("concat(1, 2, map { })", "ERR FOTY0013")]
    [InlineData("concat(1, 2, 3, string-length#1)", "ERR FOTY0013")]
    [InlineData("concat(map { }, 2, 3)", "ERR FOTY0013")]
    [InlineData("concat(1, 2, [3, 4])", "ERR XPTY0004")]
    [InlineData("concat(1, 2, [3])", "123")]
    [InlineData("concat(1, 2, <a>3</a>)", "123")]
    [InlineData("concat('a', 'b', ('c', 'd'))", "ERR XPTY0004")]
    [InlineData("let $f := function-lookup(xs:QName('fn:concat'), 3) return $f(1, 2, map { })", "ERR FOTY0013")]
    [InlineData("let $f := concat#3 return $f(1, 2, [3])", "123")]
    public async Task Each_argument_of_concat_is_converted(string query, string expected)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be(expected);
        (await RunAsync(s_loose, query)).Should().Be(expected, "a wrapper that declares item()*");
    }

    // The cardinalities of the declarations: the 4.0 forms where the functions implement them (an
    // empty $options; an empty $key of highest and lowest), 3.1 where they do not (sort's $key).
    [Theory]
    [InlineData("map:size(parse-json('{\"a\": 1}', ()))", "1")]
    [InlineData("xml-to-json(<map xmlns='http://www.w3.org/2005/xpath-functions'/>, ())", "{}")]
    [InlineData("name(json-to-xml('{}', ())/*)", "map")]
    [InlineData("parse-json('{}', 'notamap')", "ERR XPTY0004")]
    [InlineData("xml-to-json(<map xmlns='http://www.w3.org/2005/xpath-functions'/>, 'x')", "ERR XPTY0004")]
    [InlineData("json-to-xml('{}', 'x')", "ERR XPTY0004")]
    [InlineData("string-join(sort((3, 1, 2), (), ()), ',')", "ERR XPTY0004")]
    [InlineData("array:size(array:sort([3, 1], (), ()))", "ERR XPTY0004")]
    [InlineData("string-join(sort((3, 1, 2), (), abs#1), ',')", "1,2,3")]
    [InlineData("sort((3, 1, 2), (), 'x')", "ERR XPTY0004")]
    [InlineData("highest((1, 3, 2), (), ())", "3")]
    [InlineData("lowest((1, 3, 2), (), ())", "1")]
    [InlineData("highest((1, 3, 2), (), 'x')", "ERR XPTY0004")]
    [InlineData("number(('1', '2'))", "ERR XPTY0004")]
    [InlineData("distinct-values(map { })", "ERR FOTY0013")]
    [InlineData("year-from-date('2020-01-01')", "ERR XPTY0004")]
    [InlineData("year-from-date(xs:untypedAtomic('2020-01-01'))", "2020")]
    [InlineData("function-lookup('concat', 2)", "ERR XPTY0004")]
    [InlineData("sum(1, map { })", "ERR FOTY0013")]
    public async Task The_declared_types_apply(string query, string expected)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be(expected);
        (await RunAsync(s_loose, query)).Should().Be(expected, "a wrapper that declares item()*");
    }

    /// <summary>
    /// The signature of a built-in's function item is its declaration (function subtyping: a test's
    /// parameter type must be a subtype of the declared one). A wrapper's function item has the
    /// wrapper's own declaration: a wrapper that declares item()* matches more function types; only
    /// the conversion of its arguments is the built-in's.
    /// </summary>
    [Theory]
    [InlineData("sum#1 instance of function(item()*) as item()*", "False")]
    [InlineData("sum#1 instance of function(xs:anyAtomicType*) as item()*", "True")]
    [InlineData("sort#3 instance of function(item()*, xs:string?, function(*)?) as item()*", "False")]
    [InlineData("sort#3 instance of function(item()*, xs:string?, function(*)) as item()*", "True")]
    [InlineData("format-number#2 instance of function(xs:decimal?, xs:string) as xs:string", "True")]
    [InlineData("format-number#3 instance of function(xs:float?, xs:string, xs:string?) as xs:string", "True")]
    public async Task The_signature_of_a_built_in_is_its_declaration(string query, string expected)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be(expected);
        (await RunAsync(s_same, query)).Should().Be(expected, "a wrapper with the built-in's parameters");
    }

    /// <summary>
    /// fn:sum, fn:avg, fn:min and fn:max atomize their first argument in their own loop (a call of the
    /// built-in itself does not convert it first, so a million items are walked once); a wrapper's call
    /// converts it. Both give the same result. The expected values are the built-in's; a row that
    /// disagrees names a difference between the two conversions.
    /// </summary>
    [Theory]
    [InlineData("sum([1, [2, 3]])", "6")]
    [InlineData("sum((<a>1</a>, <b>2</b>))", "3")]
    [InlineData("sum(map { })", "ERR FOTY0013")]
    [InlineData("sum((1, string-length#1))", "ERR FOTY0013")]
    [InlineData("sum(())", "0")]
    [InlineData("sum((), ())", "")]
    [InlineData("sum('a')", "ERR FORG0006")]
    [InlineData("sum((xs:untypedAtomic('1'), 2))", "3")]
    [InlineData("sum((xs:dayTimeDuration('PT1S'), xs:dayTimeDuration('PT2S')))", "00:00:03")]
    [InlineData("sum([[]])", "0")]
    [InlineData("sum(<a x='1' y='2'/>/@*)", "3")]
    [InlineData("sum([1, 2], 0)", "3")]
    [InlineData("sum((), map { })", "ERR FOTY0013")]
    [InlineData("avg([1, [3]])", "2")]
    [InlineData("avg((<a>1</a>, <a>3</a>))", "2")]
    [InlineData("avg(map { })", "ERR FOTY0013")]
    [InlineData("avg((1, true#0))", "ERR FOTY0013")]
    [InlineData("avg(())", "")]
    [InlineData("avg(('a', 'b'))", "ERR FORG0006")]
    [InlineData("max([1, [5]])", "5")]
    [InlineData("max((<a>1</a>, <a>3</a>))", "3")]
    [InlineData("max(('a', 'b'))", "b")]
    [InlineData("max(map { })", "ERR FOTY0013")]
    [InlineData("max((1, 'a'))", "ERR FORG0006")]
    [InlineData("max([3, 1], 'http://www.w3.org/2005/xpath-functions/collation/codepoint')", "3")]
    [InlineData("max(('a', 'B'), 'http://www.w3.org/2005/xpath-functions/collation/codepoint')", "a")]
    [InlineData("min([3, [1]])", "1")]
    [InlineData("min((<a>3</a>, <a>1</a>))", "1")]
    [InlineData("min(map { })", "ERR FOTY0013")]
    [InlineData("min(())", "")]
    [InlineData("min((xs:untypedAtomic('2'), 1))", "1")]
    [InlineData("min(('b', 'a'), 'http://www.w3.org/2005/xpath-functions/collation/codepoint')", "a")]
    [InlineData("let $f := sum#1 return $f([1, [2, 3]])", "6")]
    [InlineData("let $f := max#1 return $f(map { })", "ERR FOTY0013")]
    [InlineData("let $f := avg#1 return $f((<a>1</a>, <a>3</a>))", "2")]
    [InlineData("let $f := min(?) return $f([3, [1]])", "1")]
    public async Task Sum_avg_min_and_max_convert_their_argument_as_the_conversion_does(string query, string expected)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be(expected, "the built-in (its own loop)");
        (await RunAsync(s_same, query)).Should().Be(expected, "a wrapper with the built-in's parameters (the conversion)");
        (await RunAsync(s_loose, query)).Should().Be(expected, "a wrapper that declares item()* (the conversion)");
    }

    /// <summary>
    /// The scan of an atomic argument tests every item: the common atomic types (integer, string, double,
    /// decimal, boolean) take one comparison, and an item of another type after them (a date, a duration,
    /// a node, an array, a map, a function item) is still found.
    /// </summary>
    [Theory]
    [InlineData("index-of((1, 2, xs:date('2020-01-01'), 3), 3)", "4")]
    [InlineData("index-of((1, 2, map { }), 1)", "ERR FOTY0013")]
    [InlineData("index-of((1, 2, <a>3</a>), '3')", "3")]
    [InlineData("index-of((xs:date('2020-01-01'), [1]), 1)", "2")]
    [InlineData("index-of((1, 'a', 2.5, 1e0, true(), xs:date('2020-01-01'), string-length#1), 1)", "ERR FOTY0013")]
    [InlineData("string-join((1, xs:date('2020-01-01'), <a>x</a>), ',')", "1,2020-01-01,x")]
    [InlineData("string-join((1, [2, 3]), ',')", "1,2,3")]
    [InlineData("string-join((1, 2, xs:untypedAtomic('u'), xs:anyURI('urn:x'), map { }), ',')", "ERR FOTY0013")]
    [InlineData("count(distinct-values((1, 1, string-length#1)))", "ERR FOTY0013")]
    [InlineData("count(distinct-values((1, xs:dayTimeDuration('PT1S'), <a>1</a>)))", "3")]
    public async Task Every_item_of_an_atomic_argument_is_tested(string query, string expected)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be(expected);
        (await RunAsync(s_loose, query)).Should().Be(expected, "a wrapper that declares item()*");
    }

    // format-number's $value is xs:numeric? (F&O 3.1 §4.7.2): a string is XPTY0004; a decimal keeps its value.
    [Theory]
    [InlineData("format-number('x', '0')", "ERR XPTY0004")]
    [InlineData("format-number('x', '0', ())", "ERR XPTY0004")]
    [InlineData("format-number(1.5, '0.0')", "1.5")]
    [InlineData("format-number(0.1, '0.0000000000000000000')", "0.1000000000000000000")]
    [InlineData("format-number(xs:untypedAtomic('2'), '0')", "2")]
    [InlineData("format-number(<a>2</a>, '0')", "2")]
    public async Task Format_number_takes_a_numeric_value(string query, string expected)
    {
        (await RunAsync(FunctionLibrary.Standard, query)).Should().Be(expected);
        (await RunAsync(s_loose, query)).Should().Be(expected, "a wrapper that declares item()*");
    }

    // A function that is not a built-in's name is not checked, as before: a host function in another
    // namespace, and one in no namespace with a built-in's local name.
    [Theory]
    [InlineData("Q{urn:host}parse-xml-fragment(['<a/>'])")]
    [InlineData("Q{}parse-xml-fragment(['<a/>'])")]
    public async Task A_function_that_has_no_built_in_s_name_is_not_checked(string query)
    {
        var library = FunctionLibrary.Standard.Copy();
        var builtIn = FunctionLibrary.Standard.Resolve(new QName(FunctionNamespaces.Fn, "parse-xml-fragment"), 1)!;
        var uri = query.StartsWith("Q{urn:host}", StringComparison.Ordinal) ? "urn:host" : "";
        library.Register(new Echo(uri));

        (await RunAsync(library, query)).Should().Be("[<a/>]", "the host function gets the array; the check of " + builtIn.Name.LocalName + " does not run");
    }

    /// <summary>A host function <c>Q{uri}parse-xml-fragment#1</c> that returns the type of what it was given.</summary>
    private sealed class Echo(string uri) : XQueryFunction
    {
        private static readonly NamespaceId s_host = new(NamespaceId.FirstUserNamespaceId + 7001);

        public override QName Name => uri.Length == 0
            ? new QName(NamespaceId.None, "parse-xml-fragment")
            : new QName(s_host, "parse-xml-fragment") { ExpandedNamespace = uri };

        public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;

        public override IReadOnlyList<FunctionParameterDef> Parameters =>
            [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

        public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, PhoenixmlDb.XQuery.Ast.ExecutionContext context)
            => ValueTask.FromResult<object?>(arguments[0] is List<object?> { Count: 1 } array ? $"[{array[0]}]" : arguments[0]?.GetType().Name);
    }
}
