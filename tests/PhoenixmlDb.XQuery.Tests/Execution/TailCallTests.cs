using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A call to a declared function in tail position runs as the caller's next iteration, not nested
/// (xquery#296): tail recursion, direct or mutual, is not limited by the function-call depth
/// (FOER0000 at 1,000). Tail positions: the body; the branches of if, typeswitch and switch; the
/// return of a let-only FLWOR; an arrow call there. A call whose result is used afterwards is not a
/// tail call, nor one to a function of another return type, whose coercion must still apply.
/// </summary>
public class TailCallTests
{
    private static Task<string> Eval(string q) => new XQueryFacade().EvaluateAsync(q, inputXml: null);

    [Theory]
    [InlineData("declare function local:n($k as xs:integer, $a as xs:integer) as xs:integer { if ($k eq 0) then $a else local:n($k - 1, $a + 1) }; local:n(100000, 0)", "100000")]
    [InlineData("declare function local:n($k) { let $j := $k - 1 return if ($k eq 0) then 'done' else local:n($j) }; local:n(100000)", "done")]
    [InlineData("declare function local:n($k) { typeswitch ($k) case xs:integer return (if ($k eq 0) then 'ts' else local:n($k - 1)) default return 'x' }; local:n(100000)", "ts")]
    [InlineData("declare function local:n($k) { switch ($k) case 0 return 'sw' default return local:n($k - 1) }; local:n(100000)", "sw")]
    [InlineData("declare function local:n($k) { if ($k eq 0) then 'arrow' else ($k - 1) => local:n() }; local:n(100000)", "arrow")]
    [InlineData("declare function local:even($n) { if ($n eq 0) then true() else local:odd($n - 1) }; declare function local:odd($n) { if ($n eq 0) then false() else local:even($n - 1) }; local:even(100001)", "false")]
    public async Task Tail_recursion_is_not_limited_by_the_call_depth(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Fact]
    public async Task A_call_whose_result_is_used_is_still_limited()
    {
        var act = () => Eval("declare function local:f($n) { if ($n eq 0) then 0 else 1 + local:f($n - 1) }; local:f(2000)");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FOER0000");
    }

    [Fact]
    public async Task A_callee_of_another_return_type_keeps_the_callers_coercion()
    {
        var act = () => Eval("declare function local:a($n) as xs:integer { if ($n eq 0) then 7 else local:b($n - 1) }; declare function local:b($n) as xs:string { string($n) }; local:a(3)");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }

    [Fact]
    public async Task The_callers_return_type_applies_to_the_last_result()
        => (await Eval("declare function local:n($k) as xs:double { if ($k eq 0) then 3 else local:n($k - 1) }; string(local:n(5000) instance of xs:double)"))
            .Should().Be("true");

    [Fact]
    public async Task An_error_deep_in_a_tail_chain_is_reported()
    {
        var act = () => Eval("declare function local:n($k) { if ($k eq 0) then error(xs:QName('local:deep'), 'at the bottom') else local:n($k - 1) }; local:n(50000)");
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("at the bottom");
    }

    // On a 256 KB thread: the tail calls take no stack per level, so a depth no stack could hold
    // as nested calls runs to the end.
    [Fact]
    public void Tail_recursion_runs_on_a_small_stack()
    {
        string? result = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = Eval("declare function local:n($k) { if ($k eq 0) then 'small' else local:n($k - 1) }; local:n(100000)")
                    .GetAwaiter().GetResult();
            }
#pragma warning disable CA1031 // reported through the assertion below
            catch (Exception e) { error = e; }
#pragma warning restore CA1031
        }, maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();
        error.Should().BeNull();
        result.Should().Be("small");
    }
}
