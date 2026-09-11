using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Built-in functions apply the cardinality half of the function conversion rules. A
/// user-declared function already raised XPTY0004 for a wrong-sized argument; a built-in
/// accepted anything, so <c>substring('abc', ())</c> returned "" and
/// <c>substring('abc', (1, 2))</c> returned "abc".
/// </summary>
public sealed class BuiltinArgumentCardinalityTests
{
    private static async Task<List<object?>> EvalAsync(string query)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return items;
    }

    [Theory]
    [InlineData("substring('abc', ())")]
    [InlineData("substring('abc', (1, 2))")]
    [InlineData("substring('abc', 1, ())")]
    [InlineData("round-half-to-even(1.5, ())")]
    [InlineData("contains('a', 'b', ())")]
    [InlineData("let $none := () return substring('abc', $none)")]
    public async Task AWrongSizedArgument_IsXPTY0004(string query)
    {
        var act = () => EvalAsync(query);
        var ex = (await act.Should().ThrowAsync<XQueryException>()).Which;
        ex.ErrorCode.Should().Be("XPTY0004");
        ex.Line.Should().Be(1, "the error names the call site, like every other built-in error");
    }

    /// <summary>
    /// These signatures were declared exactly-one where F&amp;O 3.1 says <c>?</c>; turning the
    /// check on without correcting them rejected valid calls.
    /// </summary>
    [Theory]
    [InlineData("year-from-date(())")]
    [InlineData("hours-from-duration(())")]
    [InlineData("seconds-from-time(())")]
    [InlineData("math:sqrt(())")]
    [InlineData("math:pow((), 2)")]
    [InlineData("xs:string(())")]
    [InlineData("xs:integer(())")]
    [InlineData("parse-json(())")]
    [InlineData("json-to-xml(())")]
    [InlineData("round((), 2)")]
    [InlineData("sum((), ())")]
    public async Task AnEmptyArgumentTheSpecAllows_IsAccepted(string query)
        => (await EvalAsync(query)).Should().BeEmpty();

    [Fact]
    public async Task FormatNumber_OfTheEmptySequence_IsNaN()
        => (await EvalAsync("format-number((), '0')")).Should().Equal("NaN");

    /// <summary>
    /// The audit behind the corrections, kept as a test: no built-in in the fn, math, map or
    /// array namespace declares a parameter exactly-one that F&amp;O 3.1 declares optional.
    /// Listed from the W3C function catalog for exactly the parameters found wrong.
    /// </summary>
    [Theory]
    [InlineData("year-from-date", 1, 0)]
    [InlineData("month-from-dateTime", 1, 0)]
    [InlineData("days-from-duration", 1, 0)]
    [InlineData("format-number", 2, 0)]
    [InlineData("format-number", 3, 0)]
    [InlineData("parse-json", 2, 0)]
    [InlineData("round", 2, 0)]
    [InlineData("sum", 2, 1)]
    public void CorrectedSignatures_DeclareTheParameterOptional(string name, int arity, int index)
    {
        var fn = FunctionLibrary.Standard.GetAllFunctions()
            .Single(f => f.Name.LocalName == name && f.Parameters.Count == arity
                && f.Name.Namespace == FunctionNamespaces.Fn);
        fn.Parameters[index].Type!.Occurrence.Should().Be(PhoenixmlDb.XQuery.Ast.Occurrence.ZeroOrOne);
    }
}
