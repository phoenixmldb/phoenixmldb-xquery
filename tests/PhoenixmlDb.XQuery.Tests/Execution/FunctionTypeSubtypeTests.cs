using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Tests for function-type subtype checking, covering map/array universal function types.
/// See XQuery 3.1 §17.1.2: every map(K,V) IS-A function(xs:anyAtomicType) as item()*.
/// </summary>
public class FunctionTypeSubtypeTests
{
    private static async System.Threading.Tasks.Task<object?> ExecuteFirstAsync(
        IAsyncEnumerable<object?> plan)
    {
        await foreach (var item in plan)
            return item;
        return null;
    }

    [Fact]
    public async System.Threading.Tasks.Task MapMatchesUniversalFunctionType()
    {
        // Any concrete map IS-A function(xs:anyAtomicType) as item()*
        const string query = """map{1:"a"} instance of function(xs:anyAtomicType) as item()*""";
        var engine = new QueryEngine();
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        var result = await ExecuteFirstAsync(compiled.ExecutionPlan!.ExecuteAsync(ctx));
        result.Should().Be(true, "every map IS-A function(xs:anyAtomicType) as item()* per XQuery 3.1 §17.1.2");
    }

    [Fact]
    public async System.Threading.Tasks.Task MapTest054_FunctionTakingMapParamSubtypeCheck()
    {
        // QT3 MapTest-054 verbatim:
        // A function(function(xs:anyAtomicType) as item()*) as xs:integer IS-A
        // function(map(xs:integer, xs:string)) as xs:integer
        // because map(xs:integer, xs:string) <: function(xs:anyAtomicType) as item()*.
        const string query = """
            function($m as function(xs:anyAtomicType) as item()*) as xs:integer {map:size($m)}
            instance of function(map(xs:integer, xs:string)) as xs:integer
            """;
        var engine = new QueryEngine();
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        var result = await ExecuteFirstAsync(compiled.ExecutionPlan!.ExecuteAsync(ctx));
        result.Should().Be(true, "QT3 MapTest-054: map(xs:integer, xs:string) <: function(xs:anyAtomicType) as item()*");
    }

    [Fact]
    public async System.Threading.Tasks.Task StandardContravariance_RejectsWrongParamType()
    {
        // function(xs:integer) as xs:string is NOT a subtype of function(xs:string) as xs:string
        // because xs:string is not a subtype of xs:integer (the param direction is contravariant).
        const string query = """
            function($x as xs:integer) as xs:string { string($x) }
            instance of function(xs:string) as xs:string
            """;
        var engine = new QueryEngine();
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        var result = await ExecuteFirstAsync(compiled.ExecutionPlan!.ExecuteAsync(ctx));
        result.Should().Be(false, "function(xs:integer) as xs:string is NOT a subtype of function(xs:string) as xs:string");
    }
}
