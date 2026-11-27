using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The item-type half of the function conversion rules (XQuery 3.1 §3.1.5.2) for a static call of a
/// built-in. A static call checked only cardinality and xs:string items; a dynamic call of the same
/// function checked the item type. So <c>name('x')</c> gave "" and <c>let $f := name#1 return $f('x')</c>
/// XPTY0004; <c>array:size('x')</c> gave 0; an integer parameter given 'x' a .NET FormatException.
/// For every built-in of fn:, math:, map: and array: (but those that read resources or raise errors),
/// and each parameter whose declared item type excludes a value, one call with such a value there and
/// valid values elsewhere is XPTY0004.
/// </summary>
public class BuiltinArgumentItemTypeTests
{
    private static readonly HashSet<string> Skip = new(StringComparer.Ordinal)
    {
        "doc", "doc-available", "collection", "uri-collection", "unparsed-text", "unparsed-text-lines",
        "unparsed-text-available", "json-doc", "transform", "load-xquery-module", "environment-variable",
        "available-environment-variables", "trace", "error", "put", "message",
    };

    private static string? Good(ItemType t) => t switch
    {
        ItemType.Item or ItemType.AnyAtomicType or ItemType.Integer or ItemType.Numeric => "1",
        ItemType.Node or ItemType.Element => "<a/>",
        ItemType.Document => "document { <a/> }",
        ItemType.Attribute => "attribute a { 'v' }",
        ItemType.Text => "text { 't' }",
        ItemType.Comment => "comment { 'c' }",
        ItemType.ProcessingInstruction => "processing-instruction p { 'x' }",
        ItemType.Namespace => "namespace p { 'urn:p' }",
        ItemType.String => "'a'",
        ItemType.Boolean => "true()",
        ItemType.Decimal => "1.0",
        ItemType.Double => "1.0e0",
        ItemType.Float => "xs:float(1)",
        ItemType.Date => "xs:date('2020-01-01')",
        ItemType.DateTime => "xs:dateTime('2020-01-01T00:00:00')",
        ItemType.Time => "xs:time('00:00:00')",
        ItemType.Duration => "xs:duration('P1D')",
        ItemType.YearMonthDuration => "xs:yearMonthDuration('P1M')",
        ItemType.DayTimeDuration => "xs:dayTimeDuration('PT1S')",
        ItemType.QName => "xs:QName('a')",
        ItemType.AnyUri => "xs:anyURI('urn:a')",
        ItemType.UntypedAtomic => "xs:untypedAtomic('a')",
        ItemType.GYearMonth => "xs:gYearMonth('2020-01')",
        ItemType.GYear => "xs:gYear('2020')",
        ItemType.GMonthDay => "xs:gMonthDay('--01-01')",
        ItemType.GDay => "xs:gDay('---01')",
        ItemType.GMonth => "xs:gMonth('--01')",
        ItemType.HexBinary => "xs:hexBinary('00')",
        ItemType.Base64Binary => "xs:base64Binary('AA==')",
        ItemType.Map => "map { }",
        ItemType.Array => "[ ]",
        ItemType.Function => "true#0",
        ItemType.Empty => "()",
        _ => null,
    };

    // A value the declared type excludes: not a node for a node type, not promotable or castable
    // for an atomic type, not a function for map(*), array(*) and function(*).
    private static string? Wrong(ItemType t) => t switch
    {
        ItemType.Node or ItemType.Element or ItemType.Attribute or ItemType.Text or ItemType.Comment
            or ItemType.ProcessingInstruction or ItemType.Document or ItemType.Namespace => "'x'",
        ItemType.Map or ItemType.Array or ItemType.Function => "'x'",
        ItemType.String or ItemType.AnyUri or ItemType.UntypedAtomic => "1",
        ItemType.Boolean or ItemType.Integer or ItemType.Decimal or ItemType.Double or ItemType.Float or ItemType.Numeric
            or ItemType.Date or ItemType.DateTime or ItemType.Time or ItemType.Duration or ItemType.YearMonthDuration
            or ItemType.DayTimeDuration or ItemType.QName or ItemType.GYearMonth or ItemType.GYear or ItemType.GMonthDay
            or ItemType.GDay or ItemType.GMonth or ItemType.HexBinary or ItemType.Base64Binary => "'x'",
        _ => null,
    };

    public static TheoryData<string, string> Calls()
    {
        var data = new TheoryData<string, string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in FunctionLibrary.Standard.GetAllFunctions()
                     .OrderBy(f => FunctionNamespaces.ResolveNamespace(f.Name.Namespace)).ThenBy(f => f.Name.LocalName).ThenBy(f => f.Arity))
        {
            var uri = FunctionNamespaces.ResolveNamespace(f.Name.Namespace);
            var prefix = uri switch
            {
                "http://www.w3.org/2005/xpath-functions" => "fn",
                "http://www.w3.org/2005/xpath-functions/math" => "math",
                "http://www.w3.org/2005/xpath-functions/map" => "map",
                "http://www.w3.org/2005/xpath-functions/array" => "array",
                _ => null,
            };
            if (prefix is null || Skip.Contains(f.Name.LocalName) || !seen.Add($"{prefix}:{f.Name.LocalName}#{f.Arity}"))
                continue;
            var ps = f.Parameters;
            for (var i = 0; i < ps.Count; i++)
            {
                if (Wrong(ps[i].Type.ItemType) is not { } wrong)
                    continue;
                var args = ps.Select((p, j) => j == i ? wrong : Good(p.Type.ItemType) ?? "()");
                data.Add($"{prefix}:{f.Name.LocalName}#{f.Arity} ${ps[i].Name.LocalName} as {ps[i].Type}", $"Q{{{uri}}}{f.Name.LocalName}({string.Join(", ", args)})");
            }
        }
        return data;
    }

    [Fact]
    public void Every_checked_parameter_is_in_the_sweep()
        => Calls().Count.Should().BeGreaterThan(290, "the sweep covers the declared parameters of the library");

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task A_value_of_another_type_is_XPTY0004(string parameter, string call)
    {
        var act = () => new XQueryFacade().EvaluateAsync(call, "<x/>");

        (await act.Should().ThrowAsync<XQueryRuntimeException>("{0}: {1}", parameter, call)).Which.ErrorCode.Should().Be("XPTY0004");
    }

    // The examples of the report, on the static and the dynamic path.
    [Theory]
    [InlineData("name('x')")]
    [InlineData("let $f := name#1 return $f('x')")]
    [InlineData("array:size('x')")]
    [InlineData("substring('abc', 'x')")]
    [InlineData("array:get([1], 'x')")]
    [InlineData("local-name(map { })")]
    public async Task The_examples_of_the_report_are_XPTY0004(string query)
    {
        var act = () => new XQueryFacade().EvaluateAsync(query, "<x/>");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }

    // Guards: what the conversion rules allow still passes (atomization, untypedAtomic, promotion).
    [Theory]
    [InlineData("name(<a/>)", "a")]
    [InlineData("substring('abc', 2)", "bc")]
    [InlineData("substring('abc', xs:untypedAtomic('2'))", "bc")]
    [InlineData("abs(<n>-3</n>) eq 3", "true")]
    [InlineData("round(2.5e0, 0) eq 3", "true")]
    [InlineData("string-length(xs:anyURI('ab'))", "2")]
    [InlineData("array:size([1, 2])", "2")]
    [InlineData("count(map:keys(map { 'a': 1 }))", "1")]
    public async Task What_the_conversion_rules_allow_still_passes(string query, string expected)
        => (await new XQueryFacade().EvaluateAsync(query, "<x/>")).Should().Be(expected);

    // F&O 3.1 §14.7.3: "A type error [err:XPTY0004] occurs if the second argument is present and does not
    // match either of the types element(output:serialization-parameters)? or map(*)." The library declares
    // $params as item()?, so the check is fn:serialize's own; it was ignored and the defaults used.
    [Theory]
    [InlineData("serialize(<a/>, 'text')")]
    [InlineData("serialize(<a/>, text { 'x' })")]
    [InlineData("serialize(<a/>, [ map { } ])")]
    [InlineData("serialize(<a/>, 1)")]
    [InlineData("let $f := serialize#2 return $f(<a/>, 'text')")]
    [InlineData("let $f := serialize#2 return $f(<a/>, text { 'x' })")]
    public async Task Serialize_parameters_that_are_not_an_element_or_a_map_are_XPTY0004(string query)
    {
        var act = () => new XQueryFacade().EvaluateAsync(query, "<x/>");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }

    [Theory]
    [InlineData("serialize(<a>x</a>, ())", "<a>x</a>")]
    [InlineData("serialize(<a>x</a>, map { 'method': 'text' })", "x")]
    [InlineData("serialize(<a>x</a>, <output:serialization-parameters xmlns:output='http://www.w3.org/2010/xslt-xquery-serialization'><output:method value='text'/></output:serialization-parameters>)", "x")]
    public async Task Serialize_parameters_that_are_an_element_a_map_or_empty_still_pass(string query, string expected)
        => (await new XQueryFacade().EvaluateAsync(query, "<x/>")).Should().Be(expected);
}
