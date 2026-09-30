using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// Argument and range errors of the formatting functions and decimal arithmetic, each checked
/// against the code the specification assigns (W3C QT3 format-date/-time/-dateTime and
/// format-number cases).
/// </summary>
public class FormatArgumentErrorTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string> CodeOf(string query)
    {
        var act = () => _facade.EvaluateAsync(query);
        var ex = (await act.Should().ThrowAsync<Exception>()).Which;
        // context.Error wraps the code with a location in a different exception type; read the
        // ErrorCode property wherever it is, as the conformance harness does.
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e.GetType().GetProperty("ErrorCode")?.GetValue(e) is string { Length: > 0 } code)
                return code;
        return ex.Message;
    }

    /// <summary>Function conversion casts xs:untypedAtomic, never xs:string (format-date-inpt-er1).</summary>
    [Theory]
    [InlineData("format-date('abc', '[D]')")]
    [InlineData("format-date('2020-01-01', '[D]')")]
    [InlineData("format-time('10:00:00', '[H]')")]
    [InlineData("format-dateTime('2020-01-01T10:00:00', '[H]')")]
    public async Task A_string_value_is_XPTY0004(string query) =>
        (await CodeOf(query)).Should().Be("XPTY0004");

    [Fact]
    public async Task An_untyped_value_is_still_cast() =>
        (await _facade.EvaluateAsync("format-date(xs:untypedAtomic('2020-03-04'), '[D]')")).Should().Be("4");

    /// <summary>language, calendar and place are xs:string? (format-date-inpt-er3).</summary>
    [Theory]
    [InlineData("format-date(current-date(), '[bla]', 'en', (), 5)")]
    [InlineData("format-dateTime(current-dateTime(), '[D]', 3, (), ())")]
    public async Task A_non_string_option_is_XPTY0004(string query) =>
        (await CodeOf(query)).Should().Be("XPTY0004");

    [Theory]
    [InlineData("format-date(current-date(), '[yY]')", "FOFD1340")]
    [InlineData("format-date(current-date(), '[H]')", "FOFD1350")]
    [InlineData("format-time(current-time(), '[Y]')", "FOFD1350")]
    public async Task Picture_errors_use_the_FOFD_codes(string query, string code) =>
        (await CodeOf(query)).Should().Be(code);

    /// <summary>$value is xs:numeric?, checked before the picture (numberformat906InputErr).</summary>
    [Fact]
    public async Task Format_number_of_a_string_is_XPTY0004() =>
        (await CodeOf("format-number('abc', '000.##0')")).Should().Be("XPTY0004");

    [Theory]
    [InlineData("xs:decimal('10000000000000000000000000000') * 10")]
    [InlineData("xs:decimal('70000000000000000000000000000') + xs:decimal('70000000000000000000000000000')")]
    [InlineData("xs:decimal('-70000000000000000000000000000') - xs:decimal('70000000000000000000000000000')")]
    [InlineData("xs:decimal('70000000000000000000000000000') div 0.001")]
    [InlineData("format-number(000123456789012345678901234567890.1, '#')")]
    public async Task Decimal_overflow_is_FOAR0002(string query) =>
        (await CodeOf(query)).Should().Contain("FOAR0002");

    [Fact]
    public async Task Decimal_arithmetic_in_range_stays_decimal() =>
        (await _facade.EvaluateAsync("(xs:decimal('1000000000000000000000000000') * 10) instance of xs:decimal")).Should().Be("true");

    /// <summary>A supplementary-plane zero-digit, given as a character reference (numberformat71).</summary>
    [Fact]
    public async Task A_supplementary_zero_digit_is_accepted() =>
        (await _facade.EvaluateAsync(
            "declare default decimal-format zero-digit=\"&#66720;\"; string-to-codepoints(format-number(12, '&#66720;&#66720;')) => string-join(',')"))
            .Should().Be("66721,66722");

    /// <summary>
    /// XPath 1.0 compatibility mode converts a numeric argument with fn:number(), so a string is
    /// NaN there rather than XPTY0004 (numberformat38).
    /// </summary>
    [Fact]
    public async Task Backwards_compatible_mode_converts_a_string_to_NaN()
    {
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine();
        var compiled = engine.Compile("format-number('foo', '###')");
        var ctx = engine.CreateContext();
        ctx.BackwardsCompatible = true;
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(i);
        items.Should().Equal("NaN");
    }
}
