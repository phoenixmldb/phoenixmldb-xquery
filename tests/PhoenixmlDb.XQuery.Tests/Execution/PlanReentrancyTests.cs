using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A compiled plan is executed more than once at the same time: by concurrent callers that share
/// it, and, inside ONE execution, by a recursive function whose body is re-entered while the
/// caller's iteration is suspended. Per-execution state (a count clause's counter, a while
/// clause's termination) kept on the operators was shared by all of them and gave wrong answers
/// with no error.
/// </summary>
public sealed class PlanReentrancyTests
{
    private static async Task<string> Run(string query)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);
        return string.Join(" ", items);
    }

    // The inner call numbered its tuples with the caller's counter, so the caller resumed at 4.
    [Fact]
    public async Task Count_clause_in_a_recursive_function_numbers_each_call_separately()
    {
        var r = await Run("""
            declare function local:f($n) {
              string-join(for $x in ("a", "b", "c") count $c
                          return if ($n gt 0 and $x eq "b") then $c || "[" || local:f($n - 1) || "]" else string($c), " ")
            };
            local:f(2)
            """);
        r.Should().Be("1 2[1 2[1 2 3] 3] 3");
    }

    // The inner call's while clause ending its own iteration also ended the caller's.
    [Fact]
    public async Task While_clause_ending_in_a_recursive_call_does_not_end_the_caller()
    {
        var r = await Run("""
            declare function local:g($n) {
              string-join(for $x in 1 to 4 while ($n gt 0 or $x le 1)
                          return if ($n gt 0 and $x eq 2) then "(" || local:g($n - 1) || ")" else string($x), " ")
            };
            local:g(1)
            """);
        r.Should().Be("1 (1) 3 4");
    }

    // A count clause before an order by is numbered on the materialisation path.
    [Fact]
    public async Task Count_clause_before_order_by_still_numbers_tuples()
    {
        var r = await Run("for $x in (30, 10, 20) count $c order by $x return $c");
        r.Should().Be("2 3 1");
    }

    // One compiled plan, run concurrently with a fresh context each time (as a compiled-plan
    // cache would). Measured on 2.5.1: 3194 of 3200 count results and 2403 of 3200 while
    // results were wrong.
    [Theory]
    [InlineData("string-join(for $x in 1 to 20 count $c return string($c), ',')",
        "1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20")]
    [InlineData("string-join(for $x in 1 to 20 while ($x le 12) return string($x), ',')",
        "1,2,3,4,5,6,7,8,9,10,11,12")]
    public async Task One_plan_executed_concurrently_gives_every_caller_the_right_answer(string query, string expected)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue();
        var plan = compiled.ExecutionPlan!;

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            var wrong = 0;
            for (var i = 0; i < 100; i++)
            {
                var items = new List<object?>();
                await foreach (var item in plan.ExecuteAsync(engine.CreateContext()))
                    items.Add(item);
                if (string.Join("", items) != expected)
                    wrong++;
            }
            return wrong;
        })));

        results.Sum().Should().Be(0, "every concurrent execution of the shared plan must give the same answer");
    }
}
