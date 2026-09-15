using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.Xdm;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// QueryExecutionContext.DynamicCallDepth tells a host function whether it is running inside a dynamic function call:
/// a call through a function item, or an inline function body. XSLT needs it — current-output-uri() is absent inside a
/// dynamic call (XSLT 3.0, spec bug 30411) — and nothing in the context distinguished the two.
/// </summary>
public sealed class DynamicCallDepthTests
{
    private sealed class DepthProbeFunction : PhoenixmlDb.XQuery.Ast.XQueryFunction
    {
        public override QName Name => new(FunctionNamespaces.Fn, "depth-probe");
        public override PhoenixmlDb.XQuery.Ast.XdmSequenceType ReturnType => PhoenixmlDb.XQuery.Ast.XdmSequenceType.OptionalString;
        public override IReadOnlyList<PhoenixmlDb.XQuery.Ast.FunctionParameterDef> Parameters => [];

        public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, PhoenixmlDb.XQuery.Ast.ExecutionContext context)
            => ValueTask.FromResult<object?>(((QueryExecutionContext)context).DynamicCallDepth.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<List<string?>> RunAsync(string query)
    {
        var lib = FunctionLibrary.Standard.Copy();
        lib.Register(new DepthProbeFunction());
        var results = new List<string?>();
        await foreach (var item in new QueryEngine(functions: lib).ExecuteAsync(query))
            results.Add(item?.ToString());
        return results;
    }

    [Fact]
    public async Task A_static_call_is_at_depth_zero()
        => (await RunAsync("fn:depth-probe()")).Should().Equal("0");

    [Fact]
    public async Task A_call_through_a_function_reference_is_a_dynamic_call()
        => (await RunAsync("let $f := fn:depth-probe#0 return $f()")).Should().Equal("1");

    [Fact]
    public async Task An_inline_function_body_is_a_dynamic_call()
        => (await RunAsync("let $f := function() { fn:depth-probe() } return $f()")).Should().Equal("2");

    [Fact]
    public async Task An_inline_function_a_higher_order_function_invokes_is_a_dynamic_call()
        => (await RunAsync("fn:for-each(1, function($x) { fn:depth-probe() })")).Should().Equal("1");

    [Fact]
    public async Task The_depth_falls_back_after_the_call()
        => (await RunAsync("let $f := function() { fn:depth-probe() } return ($f(), fn:depth-probe())"))
            .Should().Equal("2", "0");
}
