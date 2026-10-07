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
/// <para>Asserted as LATENCY — cancel at 200 ms, stopped within 5 s — because "it threw
/// OperationCanceledException eventually" is also true of a query that ran to completion
/// first. A 20 s watchdog fails a shape that never stops, rather than holding the suite.</para>
/// <para>The input sequences are bound as external variables, pre-built. Built inside the
/// query, `1 to N` polls the token itself, so cancellation landed while the input was still
/// being materialised and every shape passed whether or not its own loop ever polled — the
/// first version of this test measured nothing but the range operator.</para>
/// </remarks>
[Collection(TimingSensitiveTests.Name)]
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
        { "fold-left", "fold-left($tenM, 0, function($a, $b) { $a + ($b * 7 + 3) mod 11 })" },
        { "for-each", "count(for-each($tenM, function($x) { ($x * 7 + 3) mod 11 }))" },
        { "filter", "count(filter($tenM, function($x) { ($x * 7 + 3) mod 11 = 0 }))" },
        { "sort with key", "count(sort($tenM, (), function($x) { ($x * 7 + 3) mod 11 }))" },
        { "for clause", "count(for $x in $tenM return ($x * 7 + 3) mod 11)" },
        { "simple map", "count($tenM ! ((. * 7 + 3) mod 11))" },
        // A running .NET regex match cannot see the token: catastrophic backtracking (time
        // exponential in the run of a's) ran on for minutes after cancellation. The match timeout
        // (1 s in these limits) is how a running match stops, and it then reports cancellation.
        { "regex backtracking, matches", "matches(string-join((1 to 40) ! 'a', ''), '(a+)+b')" },
        { "regex backtracking, analyze-string", "count(analyze-string(string-join((1 to 40) ! 'a', ''), '(a+)+b')/*)" },
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
        // The regex shapes stop at the match timeout, so it must sit well inside the 5 s bound.
        var limits = new QueryExecutionLimits { MaxResultItems = 50_000_000, RegexMatchTimeout = TimeSpan.FromSeconds(1) };
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
        var finished = await System.Threading.Tasks.Task.WhenAny(run, System.Threading.Tasks.Task.Delay(20_000));
        sw.Stop();

        finished.Should().BeSameAs(run, $"{shape}: still running 20 s after cancellation was requested at 200 ms");
        (await run).Should().BeTrue(
            $"{shape}: a query that finishes before noticing the token has not been cancelled (ran {sw.ElapsedMilliseconds} ms)");
        // 5 s, not tighter: fixed shapes stop within ~30 ms of the request on a workstation but
        // took up to ~2 s on a 2-core CI runner (fib, which polls every call, included — so that
        // is scheduling and GC, not a missed poll). The bodies are weighted so that the
        // unfixed code takes far longer than this bound; see the class remarks.
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, $"{shape}: cancellation was requested at 200 ms");
    }

    /// <summary>
    /// Uncancelled, a catastrophically backtracking match stops at the match timeout with
    /// FOER0000 instead of running on. Without a timeout this case runs for hours.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task RegexMatch_PastTheTimeLimit_IsFOER0000()
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile("replace(string-join((1 to 40) ! 'a', ''), '(a+)+b', 'x')");
        compiled.Success.Should().BeTrue();
        var ctx = engine.CreateContext(limits: new QueryExecutionLimits { RegexMatchTimeout = TimeSpan.FromMilliseconds(500) });

        var run = System.Threading.Tasks.Task.Run(async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
        });
        var finished = await System.Threading.Tasks.Task.WhenAny(run, System.Threading.Tasks.Task.Delay(20_000));

        finished.Should().BeSameAs(run, "the match should stop at its 500 ms limit");
        var act = async () => await run;
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Functions.XQueryException>()).Which.ErrorCode.Should().Be("FOER0000");
    }

    /// <summary>
    /// A sort checks the token as it compares. List.Sort wraps the comparer's exception, and
    /// the caller must see the cancellation itself, not "Failed to compare two elements".
    /// </summary>
    [Fact]
    public void Sort_WithCancelledToken_ThrowsOperationCanceled()
    {
        var items = Enumerable.Range(0, 5000).Select(i => (i * 7919) % 5003).ToList();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => PhoenixmlDb.XQuery.Functions.SortHelper.Sort(items, (a, b) => a.CompareTo(b), cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    /// <summary>
    /// A module loaded with fn:load-xquery-module runs under the calling query's limits. Its
    /// context was created with none, so a regex in a function it returned, or in one of its
    /// variables, ignored RegexMatchTimeout, where the same module imported statically obeyed it.
    /// </summary>
    [Theory]
    [InlineData("declare function m:f() { matches(string-join((1 to 40) ! 'a', ''), '(a+)+b') };",
        "?functions(QName('urn:m', 'f'))?0()")]
    [InlineData("declare variable $m:v := matches(string-join((1 to 40) ! 'a', ''), '(a+)+b');",
        "?variables(QName('urn:m', 'v'))")]
    public async System.Threading.Tasks.Task DynamicallyLoadedModule_ObeysTheCallersRegexMatchTimeout(string declaration, string use)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"phoenixmldb-dynmod-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var modulePath = System.IO.Path.Combine(dir, "m.xqm");
            await System.IO.File.WriteAllTextAsync(modulePath, "module namespace m = 'urn:m'; " + declaration);
            var env = new XdmDocumentStore();
            var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
            var compiled = engine.Compile(
                $"load-xquery-module('urn:m', map {{ 'location-hints': '{new Uri(modulePath).AbsoluteUri}' }}){use}");
            compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
            var ctx = engine.CreateContext(limits: new QueryExecutionLimits { RegexMatchTimeout = TimeSpan.FromMilliseconds(500) });

            var run = System.Threading.Tasks.Task.Run(async () =>
            {
                await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
            });
            var finished = await System.Threading.Tasks.Task.WhenAny(run, System.Threading.Tasks.Task.Delay(20_000));

            finished.Should().BeSameAs(run, "the match should stop at its 500 ms limit");
            var act = async () => await run;
            (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("time limit");
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch (System.IO.IOException) { }
        }
    }
}
