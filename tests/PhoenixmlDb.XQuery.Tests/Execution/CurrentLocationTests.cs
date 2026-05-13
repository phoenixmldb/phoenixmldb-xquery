using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Phase A of the source-location audit: verifies the QueryExecutionContext
/// CurrentLocation push/pop infrastructure and Error() factory helpers.
/// </summary>
public class CurrentLocationTests
{
    private static QueryExecutionContext NewContext()
        => new(new ContainerId(1), FunctionLibrary.Standard);

    [Fact]
    public void CurrentLocation_starts_null()
    {
        using var ctx = NewContext();
        ctx.CurrentLocation.Should().BeNull();
    }

    [Fact]
    public void PushLocation_installs_then_pops()
    {
        using var ctx = NewContext();
        var loc = new SourceLocation(10, 5, 0, 0) { Module = "test.xq" };
        using (ctx.PushLocation(loc))
        {
            ctx.CurrentLocation.Should().BeSameAs(loc);
        }
        ctx.CurrentLocation.Should().BeNull();
    }

    [Fact]
    public void PushLocation_null_is_no_op()
    {
        using var ctx = NewContext();
        var outer = new SourceLocation(1, 1, 0, 0);
        using (ctx.PushLocation(outer))
        {
            using (ctx.PushLocation(null))
            {
                ctx.CurrentLocation.Should().BeSameAs(outer); // null didn't shadow
            }
            ctx.CurrentLocation.Should().BeSameAs(outer);
        }
    }

    [Fact]
    public void PushLocation_nests_and_restores()
    {
        using var ctx = NewContext();
        var a = new SourceLocation(1, 1, 0, 0) { Module = "a.xq" };
        var b = new SourceLocation(2, 2, 0, 0) { Module = "b.xq" };
        using (ctx.PushLocation(a))
        {
            using (ctx.PushLocation(b))
            {
                ctx.CurrentLocation.Should().BeSameAs(b);
            }
            ctx.CurrentLocation.Should().BeSameAs(a);
        }
        ctx.CurrentLocation.Should().BeNull();
    }

    [Fact]
    public void Error_attaches_current_location()
    {
        using var ctx = NewContext();
        var loc = new SourceLocation(42, 7, 0, 0) { Module = "main.xq" };
        using var _ = ctx.PushLocation(loc);
        var ex = ctx.Error("XPTY0004", "type mismatch");
        ex.ErrorCode.Should().Be("XPTY0004");
        ex.Module.Should().Be("main.xq");
        ex.Line.Should().Be(42);
        ex.Column.Should().Be(7);
        ex.Message.Should().Contain("[main.xq:42:7]");
    }

    [Fact]
    public void Error_with_no_location_returns_locationless_exception()
    {
        using var ctx = NewContext();
        var ex = ctx.Error("FOER0000", "boom");
        ex.Module.Should().BeNull();
        ex.Line.Should().BeNull();
        ex.Column.Should().BeNull();
        ex.Message.Should().Be("boom");
    }

    [Fact]
    public void Error_with_inner_exception_preserves_inner_and_location()
    {
        using var ctx = NewContext();
        var loc = new SourceLocation(3, 4, 0, 0) { Module = "x.xq" };
        using var _ = ctx.PushLocation(loc);
        var inner = new InvalidOperationException("root cause");
        var ex = ctx.Error("FOER0000", "wrapped", inner);
        ex.InnerException.Should().BeSameAs(inner);
        ex.Line.Should().Be(3);
    }

    // Phase D8: SourceLocation.Length is computed from EndIndex - StartIndex + 1
    // (EndIndex is inclusive, matching ANTLR StopIndex). LSP adapters need this
    // to size diagnostic squiggles.
    [Fact]
    public void SourceLocation_Length_is_inclusive_range_size()
    {
        // A 5-character token starting at index 10 spans indices 10..14 → length 5.
        var loc = new SourceLocation(1, 1, 10, 14);
        loc.Length.Should().Be(5);
    }

    [Fact]
    public void SourceLocation_Length_is_zero_for_degenerate_range()
    {
        // EndIndex < StartIndex → degenerate; Length is 0 (not negative).
        var loc = new SourceLocation(1, 1, 10, 9);
        loc.Length.Should().Be(0);
    }

    [Fact]
    public void SourceLocation_Length_is_one_for_single_character_token()
    {
        var loc = new SourceLocation(1, 1, 10, 10);
        loc.Length.Should().Be(1);
    }

    // Phase D7: XQueryException.RelatedLocations defaults to empty (no related
    // locations) and accepts a list via init-only when populated.
    [Fact]
    public void XQueryException_RelatedLocations_defaults_to_empty()
    {
        var ex = new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0004", "x");
        ex.RelatedLocations.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void XQueryException_RelatedLocations_round_trips_when_populated()
    {
        // Simulate a "expected element foo, got bar" diagnostic with the offending
        // input node's source position carried as a related location.
        var primary = new SourceLocation(10, 20, 0, 0) { Module = "stylesheet.xsl" };
        var related = new SourceLocation(50, 5, 0, 0) { Module = "input.xml" };
        var ex = new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0004", "type mismatch", primary)
        {
            RelatedLocations = new[] { related }
        };
        ex.Module.Should().Be("stylesheet.xsl");
        ex.RelatedLocations.Should().ContainSingle().Which.Module.Should().Be("input.xml");
    }
}
