using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Reported by Martin Honnen (2026-08-26) against XSpec's format-xspec-report.xsl:
/// <c>format-dateTime(@date, '[D] [MNn] [Y] at [H01]:[m01]')</c> raised
/// <c>XPTY0004 … Expected xs:dateTime, got XsUntypedAtomic</c>.
///
/// Function conversion rules (XPath 3.1 §3.1.5.2) CAST xs:untypedAtomic to the declared
/// parameter type, so an untyped attribute holding a dateTime is the ORDINARY case. Atomizing
/// a node yields XsUntypedAtomic — never string — so the <c>string s =&gt;</c> arm missed it
/// and it fell to the throw. Identical in shape to the accumulator CoerceAtomicValue bug fixed
/// the day before.
///
/// The scan that found these six also flagged eight cast sites in PhysicalOperators with the
/// same shape; those are NOT fixed, because untypedAtomic is unwrapped before reaching them
/// and every probe of xs:dateTime(@a) / xs:date(...) / xs:duration(...) already passed.
/// </summary>
public class FormatDateTimeUntypedAtomicTests
{
    private readonly XQueryFacade _facade = new();
    private async Task<string> Eval(string xq) => await _facade.EvaluateAsync(xq);

    private const string Untyped = "xs:untypedAtomic('2026-08-26T11:58:04.075+02:00')";

    [Fact]
    public async Task Format_dateTime_casts_untypedAtomic()
        => (await Eval($"format-dateTime({Untyped}, '[D] [MNn] [Y] at [H01]:[m01]')"))
            .Should().Be("26 August 2026 at 11:58");

    [Fact]
    public async Task Format_date_casts_untypedAtomic()
        => (await Eval($"format-date({Untyped}, '[Y]-[M01]-[D01]')")).Should().Be("2026-08-26");

    [Theory]
    [InlineData("format-dateTime")]
    [InlineData("format-date")]
    public async Task Five_argument_overloads_cast_untypedAtomic(string fn)
        => (await Eval($"{fn}({Untyped}, '[Y]', 'en', (), ())")).Should().Be("2026");

    [Fact]
    public async Task Format_time_casts_untypedAtomic()
        => (await Eval("format-time(xs:untypedAtomic('11:58:04'), '[H01]:[m01]')")).Should().Be("11:58");

    /// <summary>
    /// A dateTime lexical form is NOT a valid xs:time, so this must still fail — but as
    /// FORG0001, which is what casting an invalid lexical form raises. It used to surface as a
    /// raw .NET FormatException with no error code: the same "error names the wrong thing"
    /// fault as the XPTY0004 beside it, and easy to hit once untyped values reached the parse.
    /// </summary>
    [Fact]
    public async Task Invalid_lexical_form_is_FORG0001_not_a_clr_exception()
    {
        var act = async () => await Eval($"format-time({Untyped}, '[H01]')");
        // Functions.XQueryException, not Execution.XQueryRuntimeException — context.Error()
        // raises the former. Two exception types carry XQuery error codes; asserting the wrong
        // one is easy, which is an argument for a common base they do not currently share.
        var ex = await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Functions.XQueryException>();
        ex.Which.ErrorCode.Should().Be("FORG0001");
    }

    /// <summary>Typed and empty arguments must keep working — the arm was added, not replaced.</summary>
    [Theory]
    [InlineData("format-dateTime(xs:dateTime('2026-08-26T11:58:04Z'), '[Y]')", "2026")]
    [InlineData("format-dateTime((), '[Y]')", "")]
    [InlineData("format-date(xs:date('2026-08-26'), '[Y]')", "2026")]
    public async Task Typed_and_empty_arguments_are_unaffected(string q, string expected)
        => (await Eval(q)).Should().Be(expected);
}
