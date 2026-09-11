using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// `contains text` end to end. There were no full-text tests at all, and QT3 has no full-text
/// coverage, so three defects coexisted unseen: every full-text query threw at compile time
/// (xquery#13), only the last item of the source was searched, and search words written as
/// <c>{expr}</c> were never evaluated and matched everything.
/// </summary>
public sealed class FullTextContainsTests
{
    private static async Task<List<object?>> EvalAsync(string query, params (string Name, object? Value)[] externals)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        foreach (var (name, value) in externals) ctx.SetExternalVariable(name, value);
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return items;
    }

    [Theory]
    [InlineData("\"walrus tusk\" contains text \"walrus\"", true)]
    [InlineData("\"walrus tusk\" contains text \"seal\"", false)]
    [InlineData("<n><b>walrus tusk</b></n>/b contains text \"walrus\"", true)]
    [InlineData("\"walrus tusk\" contains text \"walrus\" ftand \"tusk\"", true)]
    [InlineData("\"walrus tusk\" contains text \"walrus\" ftand ftnot \"tusk\"", false)]
    [InlineData("\"walrus tusk\" contains text \"walrus tusk\" phrase", true)]
    [InlineData("\"walrus tusk\" contains text \"tusk walrus\" phrase", false)]
    public async Task Literal(string query, bool expected)
        => (await EvalAsync(query)).Should().Equal(expected);

    [Fact]
    public async Task InAPredicate_AndAWhereClause()
    {
        (await EvalAsync("(<n><b>walrus tusk</b></n>, <n><b>seal</b></n>)[b contains text \"walrus\"]/b/string()"))
            .Should().Equal("walrus tusk");
        (await EvalAsync("for $n in (<n><b>walrus</b></n>, <n><b>seal</b></n>) where $n/b contains text \"seal\" return $n/b/string()"))
            .Should().Equal("seal");
    }

    [Theory]
    [InlineData("(\"seal\", \"walrus tusk\") contains text \"seal\"", true)]
    [InlineData("(\"walrus tusk\", \"seal\") contains text \"walrus\"", true)]
    [InlineData("(<b>seal</b>, <b>walrus</b>) contains text \"seal\"", true)]
    [InlineData("() contains text \"seal\"", false)]
    public async Task EveryItemOfTheSourceIsSearched(string query, bool expected)
        => (await EvalAsync(query)).Should().Equal(expected);

    [Theory]
    [InlineData("\"walrus tusk\" contains text {\"seal\"}", false)]
    [InlineData("\"walrus tusk\" contains text {\"tusk\"}", true)]
    [InlineData("let $t := \"tusk\" return \"walrus tusk\" contains text {$t}", true)]
    [InlineData("\"walrus tusk\" contains text {(\"seal\", \"tusk\")} any", true)]
    [InlineData("\"walrus tusk\" contains text {(\"seal\", \"tusk\")} all", false)]
    [InlineData("\"walrus tusk\" contains text {()}", false)]
    [InlineData("\"walrus tusk\" contains text \"\"", false)]
    public async Task SearchWordsFromAnExpression(string query, bool expected)
        => (await EvalAsync(query)).Should().Equal(expected);

    [Theory]
    [InlineData("walrus", true)]
    [InlineData("seal", false)]
    public async Task SearchWordsFromAnExternalVariable(string terms, bool expected)
    {
        // The shape a database search API uses: the query is fixed, the terms are a parameter.
        var items = await EvalAsync("declare variable $q external; \"walrus tusk\" contains text {$q}", ("q", terms));
        items.Should().Equal(expected);
    }
}
