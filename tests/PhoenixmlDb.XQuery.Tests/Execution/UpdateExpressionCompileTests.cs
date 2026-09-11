using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Insert, delete, rename and both replaces inherited <c>Accept =&gt; default!</c> from
/// UpdateExpression, so any analysis pass that visited one threw a NullReferenceException at
/// compile time (xquery#15). copy/modify looked fine only because TransformExpression had its
/// own Accept and its children were never walked.
/// </summary>
public sealed class UpdateExpressionCompileTests
{
    /// <summary>
    /// The discriminating case. Inside copy/modify the primitives are never reached by the
    /// rewriting passes, so those passed even with the stub; a primitive at the top level of a
    /// query is visited, and threw.
    /// </summary>
    [Theory]
    [InlineData("insert node <c/> into <a/>")]
    [InlineData("delete node <a><b/></a>/b")]
    [InlineData("rename node <a/> as \"z\"")]
    [InlineData("replace node <a><b/></a>/b with <q/>")]
    [InlineData("replace value of node <a><b>1</b></a>/b with \"2\"")]
    public void AnUpdatePrimitiveThatAnalysisVisits_Compiles(string query)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var act = () => engine.Compile(query);
        act.Should().NotThrow();
        act().Success.Should().BeTrue(string.Join("; ", act().Errors.Select(e => e.Message)));
    }

    [Theory]
    [InlineData("copy $c := <a><b/></a> modify insert node <c/> into $c return $c", "<a><b/><c/></a>")]
    [InlineData("copy $c := <a><b/></a> modify delete node $c/b return $c", "<a/>")]
    [InlineData("copy $c := <a><b/></a> modify rename node $c/b as \"z\" return $c", "<a><z/></a>")]
    [InlineData("copy $c := <a><b/></a> modify replace node $c/b with <q/> return $c", "<a><q/></a>")]
    [InlineData("copy $c := <a><b>1</b></a> modify replace value of node $c/b with \"2\" return $c", "<a><b>2</b></a>")]
    public async Task EveryUpdatePrimitive_CompilesAndApplies(string query, string expected)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile($"serialize({query})");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        items.Should().Equal(expected);
    }
}
