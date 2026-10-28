using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:abs, fn:ceiling, fn:floor, fn:round and fn:round-half-to-even return xs:numeric?. They were
/// declared to return xs:anyAtomicType? (round#2 and round-half-to-even#2: xs:double? and to take
/// an xs:double?), so a
/// reference to one was not an instance of a function type returning xs:numeric?
/// (QT3 ArrayTest-063, ArrayTest-083).
/// </summary>
public class NumericFunctionReturnTypeTests
{
    private static async Task<string> Eval(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("abs#1")]
    [InlineData("ceiling#1")]
    [InlineData("floor#1")]
    [InlineData("round#1")]
    [InlineData("round-half-to-even#1")]
    public async Task One_argument_form_returns_numeric(string function)
        => (await Eval($"string({function} instance of function(xs:numeric?) as xs:numeric?)")).Should().Be("true");

    [Theory]
    [InlineData("round#2")]
    [InlineData("round-half-to-even#2")]
    public async Task Two_argument_form_returns_numeric(string function)
        => (await Eval($"string({function} instance of function(xs:numeric?, xs:integer) as xs:numeric?)")).Should().Be("true");

    [Fact]
    public async Task An_array_of_them_matches_an_array_of_that_function_type()
        => (await Eval("string([floor#1, ceiling#1, round#1] instance of array(function(xs:numeric?) as xs:numeric?))")).Should().Be("true");

    [Theory]
    [InlineData("round(1.25, 1) instance of xs:decimal")]
    [InlineData("round-half-to-even(1.25, 1) instance of xs:decimal")]
    [InlineData("round(12, -1) instance of xs:integer")]
    public async Task Results_keep_the_argument_type(string query)
        => (await Eval($"string({query})")).Should().Be("true");
}
