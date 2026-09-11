using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// <c>xs:integer</c> parsed from text returned a BigInteger for EVERY value, "10" included —
/// a conditional <c>cond ? long : BigInteger</c> unifies to BigInteger — and a BigInteger as a
/// predicate value then fell through to EBV and kept every item. W3C function-0701 (a knight's
/// tour) broke on exactly that from 1.6.12, when the ternary arrived.
/// </summary>
public sealed class IntegerFromTextTests
{
    private static async Task<List<object?>> EvalAsync(string query)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(query);
        using var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return items;
    }

    [Theory]
    [InlineData("xs:integer('10')")]
    [InlineData("xs:integer(xs:untypedAtomic('10'))")]
    [InlineData("xs:integer('-9223372036854775808')")]
    public async Task AnIntegerThatFitsInLong_IsALong(string query)
    {
        // The runtime TYPE is the point: numerically, BigInteger(10) equals 10.
        var items = await EvalAsync(query);
        items.Should().ContainSingle().Which.Should().BeOfType<long>();
    }

    [Fact]
    public async Task AnIntegerWiderThanLong_StaysExact()
    {
        var items = await EvalAsync("xs:integer('99999999999999999999')");
        items.Should().ContainSingle().Which.Should().Be(System.Numerics.BigInteger.Parse("99999999999999999999"));
    }

    /// <summary>Every numeric type the engine produces selects by position, not by EBV.</summary>
    [Theory]
    [InlineData("(10,20,30)[xs:integer('2')]", 20L)]
    [InlineData("(10,20,30)[xs:float(2)]", 20L)]
    [InlineData("(10,20,30)[2.0]", 20L)]
    [InlineData("(10,20,30)[xs:double(2)]", 20L)]
    [InlineData("(10,20,30)[xs:positiveInteger(3)]", 30L)]
    [InlineData("(10,20,30)[let $i := xs:integer('1') return $i]", 10L)]
    public async Task NumericPredicate_SelectsByPosition(string query, long expected)
    {
        var items = await EvalAsync(query);
        items.Should().Equal(expected);
    }

    [Theory]
    [InlineData("(10,20,30)[xs:integer('99999999999999999999')]")]
    [InlineData("(10,20,30)[1.5]")]
    [InlineData("(10,20,30)[xs:float('NaN')]")]
    public async Task NumericPredicate_MatchingNoPosition_SelectsNothing(string query)
    {
        var items = await EvalAsync(query);
        items.Should().BeEmpty("a numeric predicate that equals no position selects nothing; EBV would keep all three");
    }

    [Fact]
    public async Task PathStepPredicate_WithParsedInteger_SelectsByPosition()
    {
        // PerNodeStepOperator carried its own copy of the positional checks.
        var items = await EvalAsync("count(<r><c/><c/><c/></r>/c[xs:integer('2')])");
        items.Should().Equal(1L);
    }
}
