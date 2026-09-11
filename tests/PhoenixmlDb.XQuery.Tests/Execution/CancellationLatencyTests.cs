using System.Diagnostics;
using System.Threading;
using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A caller's timeout must stop a CPU-bound query promptly, whatever SHAPE its hot loop has.
/// </summary>
/// <remarks>
/// <para>Asserted as LATENCY — cancel at 200 ms, stopped within 1.5 s — because "it threw
/// OperationCanceledException eventually" is also true of a query that ran to completion
/// first. A 10 s watchdog fails a shape that never stops, rather than holding the suite.</para>
/// <para>The input sequences are bound as external variables, pre-built. Built inside the
/// query, `1 to N` polls the token itself, so cancellation landed while the input was still
/// being materialised and every shape passed whether or not its own loop ever polled — the
/// first version of this test measured nothing but the range operator.</para>
/// </remarks>
public sealed class CancellationLatencyTests
{
    private static readonly object?[] TenMillion = Enumerable.Range(1, 10_000_000).Select(i => (object?)(long)i).ToArray();
    private static readonly object?[] HundredThousand = TenMillion[..100_000];
    private static readonly object?[] TwentyThousand = TenMillion[..20_000];

    public static TheoryData<string, string> Shapes => new()
    {
        // `every` polled once per 16,384 items. With a body that never polls — here one builtin
        // call; in QT3 same-key-023, map:remove and map:put — a slow body stretched the gap
        // between polls to minutes: same-key-023 overran a 30 s timeout by ~20 minutes.
        { "quantifier, non-polling body", "every $i in $hundredK satisfies deep-equal($twentyK, $twentyK)" },
        { "recursion", "declare function local:fib($n) { if ($n lt 2) then $n else local:fib($n - 1) + local:fib($n - 2) }; local:fib(40)" },
        { "fold-left", "fold-left($tenM, 0, function($a, $b) { $a + $b })" },
        { "for-each", "count(for-each($tenM, function($x) { $x * 2 }))" },
        { "filter", "count(filter($tenM, function($x) { $x mod 3 = 0 }))" },
        { "sort with key", "count(sort($tenM, (), function($x) { 0 - $x }))" },
        { "for clause", "count(for $x in $tenM return $x * 2)" },
        { "simple map", "count($tenM ! (. * 2))" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async System.Threading.Tasks.Task CpuBoundQuery_StopsPromptlyAfterCancellation(string shape, string query)
    {
        var prolog = "declare variable $tenM external; declare variable $hundredK external; declare variable $twentyK external; ";
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(prolog + query);
        compiled.Success.Should().BeTrue(shape);

        using var cts = new CancellationTokenSource();
        // The materialization cap would stop these long before cancellation is tested.
        var limits = new QueryExecutionLimits { MaxResultItems = 50_000_000 };
        var ctx = engine.CreateContext(limits: limits, cancellationToken: cts.Token);
        ctx.SetExternalVariable("tenM", TenMillion);
        ctx.SetExternalVariable("hundredK", HundredThousand);
        ctx.SetExternalVariable("twentyK", TwentyThousand);

        var run = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
                return false;
            }
            catch (OperationCanceledException)
            {
                return true;
            }
        });
        cts.CancelAfter(200);
        var sw = Stopwatch.StartNew();
        // Watchdog: a shape that never polls would otherwise hold the suite for minutes.
        var finished = await System.Threading.Tasks.Task.WhenAny(run, System.Threading.Tasks.Task.Delay(10_000));
        sw.Stop();

        finished.Should().BeSameAs(run, $"{shape}: still running 10 s after cancellation was requested at 200 ms");
        (await run).Should().BeTrue(
            $"{shape}: a query that finishes before noticing the token has not been cancelled (ran {sw.ElapsedMilliseconds} ms)");
        sw.ElapsedMilliseconds.Should().BeLessThan(1500, $"{shape}: cancellation was requested at 200 ms");
    }
}
