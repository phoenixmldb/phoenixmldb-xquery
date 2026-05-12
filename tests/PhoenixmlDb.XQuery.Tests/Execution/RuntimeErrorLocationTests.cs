using FluentAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Regression tests for runtime-error location info — ensures XQueryException
/// carries Module/Line/Column and the formatted Message string includes them.
/// Originated from Martin Honnen's report that XPTY0020 from Docbook TNG
/// stylesheets had no location info.
/// </summary>
public class RuntimeErrorLocationTests
{
    private static async Task<XQueryException> CaptureAsync(string query)
    {
        var engine = new QueryEngine(nodeProvider: new XdmDocumentStore());
        var compilation = engine.Compile(query);
        compilation.Success.Should().BeTrue("query should compile cleanly — the test targets RUNTIME errors");

        var ctx = engine.CreateContext();
        var act = async () =>
        {
            await foreach (var _ in compilation.ExecutionPlan!.ExecuteAsync(ctx))
            { /* drain */ }
        };

        return (await act.Should().ThrowAsync<XQueryException>()).Which;
    }

    [Fact]
    public async Task XPTY0020_on_axis_step_against_integer_includes_line_and_column()
    {
        var ex = await CaptureAsync("1/foo");

        ex.ErrorCode.Should().Be("XPTY0020");
        ex.Line.Should().Be(1);
        ex.Column.Should().NotBeNull();
        ex.Message.Should().Contain("xs:integer 1",
            "the message must surface the offending item's runtime type so users can see what the engine actually saw");
        ex.Message.Should().Contain("[line 1, col",
            "the message must be prefixed with the location so plain string logging shows it");
    }

    [Fact]
    public async Task XPTY0020_on_axis_step_against_string_includes_actual_value()
    {
        var ex = await CaptureAsync("'abc'/foo");

        ex.ErrorCode.Should().Be("XPTY0020");
        ex.Message.Should().Contain("xs:string \"abc\"");
    }

    [Fact]
    public async Task XPTY0020_in_multi_line_query_reports_line_of_offending_step()
    {
        // The path expression is on line 3; the let-binding above is fine.
        var ex = await CaptureAsync(
            "let $x := 42\n" +
            "let $y := $x\n" +
            "return $y/foo");

        ex.ErrorCode.Should().Be("XPTY0020");
        ex.Line.Should().Be(3, "the runtime error must point at the line containing the bad step, not line 1");
        ex.Message.Should().Contain("[line 3, col");
    }

    [Fact]
    public async Task XPTY0020_in_per_node_step_with_positional_predicate_includes_location()
    {
        // [1] forces the PerNodeStepOperator path (positional predicate) — separate
        // throw site from AxisNavigationOperator. Both must carry location.
        var ex = await CaptureAsync("(1, 2, 3)/foo[1]");

        ex.ErrorCode.Should().Be("XPTY0020");
        ex.Line.Should().NotBeNull("the per-node-step operator must also carry SourceLocation");
        ex.Message.Should().Contain("[line ");
    }

    [Fact]
    public async Task XQueryException_Module_field_round_trips_when_SourceLocation_has_one()
    {
        // Direct construction test — verifies the exception type itself carries Module
        // through and formats it into the message. This guards the contract that XSLT
        // (which sets SourceLocation.Module from xml:base) gets module info into errors.
        var loc = new SourceLocation(Line: 42, Column: 7, StartIndex: 100, EndIndex: 110)
        {
            Module = "file:///stylesheets/docbook/chunk.xsl"
        };
        var ex = new XQueryException("XPTY0020", "An axis step was used when the context item is not a node", loc);

        ex.Module.Should().Be("file:///stylesheets/docbook/chunk.xsl");
        ex.Line.Should().Be(42);
        ex.Column.Should().Be(7);
        ex.Message.Should().StartWith(
            "[file:///stylesheets/docbook/chunk.xsl:42:7] An axis step was used");
    }

    [Fact]
    public async Task XQueryException_without_location_keeps_legacy_message_shape()
    {
        // Backward-compat guard: existing 2-arg constructor must produce the original
        // un-prefixed message so any callers that string-match on it don't break.
        var ex = new XQueryException("XPTY0020", "An axis step was used when the context item is not a node");

        ex.Module.Should().BeNull();
        ex.Line.Should().BeNull();
        ex.Column.Should().BeNull();
        ex.Message.Should().Be("An axis step was used when the context item is not a node");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Bare_slash_with_no_context_raises_XPDY0002()
    {
        // Per XPath 3.1 §3.3.2 a leading "/" requires a context item rooted at a document.
        // Previously DocumentRootOperator silently returned the empty sequence — making
        // QT3 K2-Axes-45 (`(/, 1)[2]`) return nothing instead of either "1" or XPDY0002.
        var ex = await CaptureAsync("/");
        ex.ErrorCode.Should().Be("XPDY0002");
    }

    [Fact]
    public async Task Bare_slash_inside_sequence_raises_XPDY0002()
    {
        // K2-Axes-45 directly: (/, 1)[2]. The spec accepts either "1" or XPDY0002;
        // we choose strict evaluation, so the path operator's missing-context error
        // propagates out of the parenthesized expression.
        var ex = await CaptureAsync("(/, 1)[2]");
        ex.ErrorCode.Should().Be("XPDY0002");
    }

    // Phase B: function-library raise sites swept to use context.Error() pick up the
    // call site's location through FunctionCallOperator's PushLocation wrapper.
    [Fact]
    public async Task XPTY0004_from_index_of_carries_call_site_location()
    {
        // fn:index-of() with empty $search raises XPTY0004 from SequenceFunctions.
        // After the Phase B sweep the location must match the call-site position.
        var ex = await CaptureAsync("fn:index-of((1,2,3), ())");
        ex.ErrorCode.Should().Be("XPTY0004");
        ex.Line.Should().Be(1);
        ex.Column.Should().NotBeNull();
        ex.Message.Should().Contain("[line 1, col");
    }
}
