using System.Collections;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Optimizer;

/// <summary>
/// A step predicate whose value cannot be a number and that does not call position() or last()
/// is evaluated by its effective boolean value on the axis step itself; only a predicate that may
/// select by position takes the per-input-node operator (PerNodeStepOperator) that reads the whole
/// axis per context node. Every predicate except a boolean or string literal took it, so
/// [@a = 'b'] read the whole axis into a list for each context node. Declared types decide: a
/// function call is non-numeric only when its declared result is xs:boolean. The results are the
/// same either way.
/// </summary>
public sealed class PredicateClassifierTests
{
    private const string Doc = "<r><e a='b' n='2'><x/></e><e a='c'/><e a='b' n='1'><x/><x/></e><f/></r>";

    private static (List<object?> Items, bool PerNode) Run(string predicate, string prolog = "")
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile($"{prolog} let $d := {Doc} return $d/e[{predicate}]/string(@a)");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => $"{e.Code} {e.Message}")));
        var items = new List<object?>();
        foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()).ToBlockingEnumerable())
            items.Add(i);
        return (items, Contains<PerNodeStepOperator>(compiled.ExecutionPlan!.Root, []));
    }

    private static bool Contains<T>(object? node, HashSet<object> seen)
    {
        if (node is null || !seen.Add(node)) return false;
        if (node is T) return true;
        if (node is not PhysicalOperator) return false;
        foreach (var property in node.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length != 0) continue;
            var value = property.GetValue(node);
            if (value is PhysicalOperator op && Contains<T>(op, seen)) return true;
            if (value is IEnumerable list and not string)
                foreach (var item in list)
                    if (item is PhysicalOperator child && Contains<T>(child, seen)) return true;
        }
        return false;
    }

    [Theory]
    [InlineData("@a = 'b'", "b,b")]
    [InlineData("@n", "b,b")]
    [InlineData("x", "b,b")]
    [InlineData("not(@n)", "c")]
    [InlineData("exists(x) and @a = 'b'", "b,b")]
    [InlineData("starts-with(@a, 'c')", "c")]
    [InlineData("@a = 'b' or x", "b,b")]
    [InlineData("some $c in x satisfies true()", "b,b")]
    public async Task A_predicate_that_cannot_select_by_position_runs_on_the_step(string predicate, string expected)
    {
        await Task.CompletedTask;
        var (items, perNode) = Run(predicate);

        string.Join(',', items).Should().Be(expected);
        perNode.Should().BeFalse();
    }

    [Theory]
    [InlineData("1", "b")]
    [InlineData("last()", "b")]
    [InlineData("position() = 2", "c")]
    [InlineData("number(@n)", "")]
    [InlineData("@a = 'b' and position() gt 1", "b")]
    [InlineData("if (@n) then 1 else 2", "b,c")]
    [InlineData("local:pos()", "c", "declare function local:pos() as xs:integer { 2 };")]
    public async Task A_predicate_that_may_select_by_position_keeps_the_per_node_operator(string predicate, string expected, string prolog = "")
    {
        await Task.CompletedTask;
        var (items, perNode) = Run(predicate, prolog);

        string.Join(',', items).Should().Be(expected);
        perNode.Should().BeTrue();
    }
}
