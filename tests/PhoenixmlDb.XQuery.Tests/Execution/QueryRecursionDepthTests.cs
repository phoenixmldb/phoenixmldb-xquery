using System.Globalization;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A chain of operators is one level of the syntax tree per link, and each pass over the tree (the
/// static analysis, the type inference, the optimizer, the cost model) and the execution recursed
/// once per level. At 50,000 links Compile overflowed the stack, which ends the process, in every
/// release to 2.9.0. The queries here run on a 1 MB thread, the default on Windows.
///
/// If a guard regresses, these tests do not merely fail: the overflow aborts the test host.
///
/// They run alone: two have a watchdog, and in parallel the long chains here took the processor
/// from the rest of the suite, so on a 4-core Windows laptop a 30 s grouping watchdog that
/// passes alone in 3 s failed in every full run.
/// </summary>
[Collection(PhoenixmlDb.XQuery.Tests.TimingSensitiveTests.Name)]
public class QueryRecursionDepthTests
{
    private const int Links = 50_000;
    private const string Prolog = "declare variable $x external := 1; ";

    [Theory]
    [InlineData("+", "50001")]
    [InlineData("-", "-49999")]
    [InlineData("mod", "0")]
    [InlineData("and", "True")]
    [InlineData("or", "True")]
    [InlineData("||", null)]
    public void A_long_operator_chain_compiles_and_runs_on_a_one_megabyte_stack(string op, string? expected)
    {
        var query = Prolog + "$x" + string.Concat(Enumerable.Repeat($" {op} $x", Links));

        var (result, caught) = RunOnSmallStack(query);

        caught.Should().BeNull();
        result.Should().ContainSingle().Which.Should().Be(expected ?? new string('1', Links + 1));
    }

    [Fact]
    public void A_long_arrow_chain_compiles_and_runs_on_a_one_megabyte_stack()
    {
        var query = Prolog + "$x" + string.Concat(Enumerable.Repeat(" => abs()", Links));

        var (result, caught) = RunOnSmallStack(query);

        caught.Should().BeNull();
        result.Should().ContainSingle().Which.Should().Be("1");
    }

    // A fat arrow held its left side twice, as the arrow's operand and as the call's first
    // argument, so each pass walked 2^n paths through a chain of n arrows: 25 arrows took 10 s to
    // compile, and 40 would take days.
    [Fact]
    public void An_arrow_chain_compiles_in_time_linear_in_its_length()
    {
        var query = Prolog + "$x" + string.Concat(Enumerable.Repeat(" => abs()", 40));
        (List<string> Result, Exception? Caught) outcome = default;
        var thread = new Thread(() => outcome = Run(query)) { IsBackground = true };

        thread.Start();
        var finished = thread.Join(TimeSpan.FromSeconds(30));

        finished.Should().BeTrue("40 arrows must not take exponential time");
        outcome.Caught.Should().BeNull();
        outcome.Result.Should().ContainSingle().Which.Should().Be("1");
    }

    // The cost of each predicate counted the cardinality of the whole chain below it, so the
    // estimate took time in n^2: in XSLT, which plans an XPath expression on its own, 50,000
    // predicates took 47 s, and 100,000 take minutes. The bound is far from both that and the
    // 2 s the plan takes now.
    [Fact]
    public void A_predicate_chain_is_planned_in_time_linear_in_its_length()
    {
        var expression = new PhoenixmlDb.XQuery.Parser.XQueryParserFacade { XPath = true }
            .Parse("$x" + string.Concat(Enumerable.Repeat("[1]", 100_000)));
        Exception? caught = null;
        PhoenixmlDb.XQuery.Optimizer.OptimizationContext context = new() { Container = default };
        var thread = new Thread(() =>
        {
            try { new PhoenixmlDb.XQuery.Optimizer.QueryOptimizer().Optimize(expression, context); }
#pragma warning disable CA1031 // the outcome is examined below
            catch (Exception e) { caught = e; }
#pragma warning restore CA1031
        }, 1024 * 1024) { IsBackground = true };

        thread.Start();
        var finished = thread.Join(TimeSpan.FromSeconds(60));

        finished.Should().BeTrue("100,000 predicates must not take time in n^2 to plan");
        caught.Should().BeNull();
    }

    // Each of these recurses asynchronously once per link, with no synchronous evaluation to move
    // to a new stack. Where the stack runs out it must fail with an error a caller can catch.
    [Theory]
    [InlineData("predicates")]
    [InlineData("simple map")]
    [InlineData("unary minus")]
    [InlineData("let clauses")]
    public void A_deep_chain_with_no_synchronous_evaluation_fails_with_a_catchable_error(string shape)
    {
        var query = Prolog + shape switch
        {
            "predicates" => "$x" + string.Concat(Enumerable.Repeat("[1]", Links)),
            "simple map" => "$x" + string.Concat(Enumerable.Repeat(" ! .", Links)),
            "unary minus" => new string('-', Links) + "$x",
            _ => "for $a in $x " + string.Concat(Enumerable.Repeat("let $b := $a ", 5_000)) + "return $b",
        };

        var (result, caught) = RunOnSmallStack(query);

        if (caught is null)
            result.Should().ContainSingle().Which.Should().Be("1");
        else
            caught.Should().BeOfType<XQueryRuntimeException>().Which.ErrorCode.Should().Be("XPDY0130");
    }

    private static (List<string> Result, Exception? Caught) RunOnSmallStack(string query)
    {
        (List<string> Result, Exception? Caught) outcome = default;
        var thread = new Thread(() => outcome = Run(query), 1024 * 1024);
        thread.Start();
        thread.Join();
        return outcome;
    }

    private static (List<string> Result, Exception? Caught) Run(string query)
    {
        try
        {
            var engine = new QueryEngine();
            var compiled = engine.Compile(query);
            compiled.Success.Should().BeTrue();
            var items = engine.ExecuteToListAsync(compiled.AnalyzedExpression!).GetAwaiter().GetResult();
            return (items.Select(i => Convert.ToString(i, CultureInfo.InvariantCulture)!).ToList(), null);
        }
#pragma warning disable CA1031 // the outcome is examined by the test
        catch (Exception e)
        {
            return ([], e);
        }
#pragma warning restore CA1031
    }
}
