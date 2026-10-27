using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A prolog variable is in scope in its whole module except its own initializing expression
/// (XQuery 3.1 §4.16): declare variable $v := $v is XPST0008. It compiled and failed at run
/// time as the circularity XQDY0054 (QT3 K-InternalVariablesWith-15b). A cycle through a
/// function is a dynamic circularity and stays XQDY0054.
/// </summary>
public class VariableSelfReferenceTests
{
    private static IReadOnlyList<string> Errors(string query)
    {
        var store = new XdmDocumentStore();
        var compiled = new QueryEngine(nodeProvider: store, documentResolver: store).Compile(query);
        return compiled.Success ? [] : compiled.Errors.Select(e => e.Code).ToList();
    }

    [Theory]
    [InlineData("declare variable $v := $v; true()")]
    [InlineData("declare variable $v := 1 + $v; true()")]
    [InlineData("declare variable $v := function() { $v }; true()")]
    [InlineData("declare variable $v external := $v; true()")]
    public void A_variable_in_its_own_initializer_is_XPST0008(string query)
        => Errors(query).Should().Contain("XPST0008");

    [Theory]
    [InlineData("declare variable $v := let $v := 1 return $v; $v")]
    [InlineData("declare variable $a := $b; declare variable $b := 1; $a")]
    [InlineData("declare variable $v := 1; declare variable $w := $v; $w")]
    [InlineData("declare variable $v := local:f(); declare function local:f() { 1 }; $v")]
    public void Other_references_compile(string query)
        => Errors(query).Should().BeEmpty();

    [Fact]
    public async Task A_cycle_through_a_function_is_still_XQDY0054()
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile("declare variable $v := local:f(); declare function local:f() { $v }; $v");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext())) { }
        };
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("Circular");
    }
}
