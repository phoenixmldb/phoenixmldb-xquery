using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Deep user-function recursion must terminate with a catchable
/// <see cref="XQueryRuntimeException"/> (FOER0000), never a
/// <see cref="StackOverflowException"/> — SO aborts the test host with SIGABRT,
/// which silently killed the QT3 triage sweep mid-run on
/// <c>fn-format-number/numberformat121</c> (5000-deep
/// <c>local:timesTenToThe</c> recursion via the <c>=&gt;</c> pipeline operator).
/// The fix adds <c>RuntimeHelpers.EnsureSufficientExecutionStack()</c> to the
/// per-call EnterFunctionCall guard so the runtime throws a catchable
/// <c>InsufficientExecutionStackException</c> before native stack exhaustion.
/// </summary>
public sealed class RecursionLimitTests
{
    [Fact]
    public async System.Threading.Tasks.Task DirectRecursion_HitsLogicalGuard()
    {
        var query = """
            declare function local:r($n as xs:integer) as xs:integer {
              if ($n eq 0) then 0 else local:r($n - 1)
            };
            local:r(5000)
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Errors.Select(e => e.Message)));

        using var ctx = engine.CreateContext();
        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
        };

        var ex = await act.Should().ThrowAsync<XQueryRuntimeException>();
        ex.Which.ErrorCode.Should().Be("FOER0000");
    }

    [Fact]
    public async System.Threading.Tasks.Task PipelineRecursion_DoesNotCrashProcess()
    {
        // Exact shape of QT3 numberformat121: => threaded through deep recursion.
        // Pre-fix this exhausted the native stack before the logical guard fired
        // because each XQuery call burns ~10 .NET async frames, so depth=1000
        // already consumed the 1 MB Linux default stack. Now caught.
        var query = """
            declare function local:t($n as xs:decimal, $exp as xs:integer) as xs:decimal {
              if ($exp eq 0) then $n
              else if ($exp gt 0) then ($n*10) => local:t($exp - 1)
              else ($n div 10) => local:t($exp + 1)
            };
            local:t(1, 5000)
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue();

        using var ctx = engine.CreateContext();
        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx)) { }
        };

        var ex = await act.Should().ThrowAsync<XQueryRuntimeException>();
        ex.Which.ErrorCode.Should().Be("FOER0000");
    }
}
