using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Regression tests for date/dateTime ± duration arithmetic that crosses the proleptic-Gregorian
/// year-1 boundary. Mirrors the QT3 op-*-duration-*-date/dateTime boundary cases that previously
/// threw FODT0002 (overflow) instead of yielding a year-0/negative-year result.
/// </summary>
public class ProlepticDateDurationTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task SubtractDayTimeDuration_FromDate_CrossesYearOne()
    {
        // op-subtract-dayTimeDuration-from-date-8
        var r = await _facade.EvaluateAsync("string(xs:date(\"0001-01-01Z\") - xs:dayTimeDuration(\"P11DT02H02M\"))");
        r.Should().Be("0000-12-20Z");
    }

    [Fact]
    public async Task AddNegativeDayTimeDuration_ToDate_CrossesYearOne()
    {
        // op-add-dayTimeDuration-to-date-8
        var r = await _facade.EvaluateAsync("string(xs:date(\"0001-01-01Z\") + xs:dayTimeDuration(\"-P11DT02H02M\"))");
        r.Should().Be("0000-12-20Z");
    }

    [Fact]
    public async Task AddNegativeDayTimeDuration_ToDateTime_CrossesYearOne()
    {
        // op-add-dayTimeDuration-to-dateTime-8
        var r = await _facade.EvaluateAsync("string(xs:dateTime(\"0001-01-01T11:11:11Z\") + xs:dayTimeDuration(\"-P11DT02H02M\"))");
        r.Should().Be("0000-12-21T09:09:11Z");
    }

    [Fact]
    public async Task AddNegativeYearMonthDuration_ToDate_CrossesIntoNegativeYear()
    {
        // op-add-yearMonthDuration-to-date-8
        var r = await _facade.EvaluateAsync("string(xs:date(\"0001-01-01Z\") + xs:yearMonthDuration(\"-P20Y07M\"))");
        r.Should().Be("-0020-06-01Z");
    }

    [Fact]
    public async Task AddNegativeYearMonthDuration_ToDateTime_CrossesIntoNegativeYear()
    {
        // op-add-yearMonthDuration-to-dateTime-8
        var r = await _facade.EvaluateAsync("string(xs:dateTime(\"0001-01-01T01:01:01Z\") + xs:yearMonthDuration(\"-P20Y07M\"))");
        r.Should().Be("-0020-06-01T01:01:01Z");
    }

    [Fact]
    public async Task SubtractNegativeYearMonthDuration_FromDate_StaysPositive()
    {
        // op-subtract-yearMonthDuration-from-date-8
        var r = await _facade.EvaluateAsync("string(xs:date(\"0001-01-01Z\") - xs:yearMonthDuration(\"-P20Y07M\"))");
        r.Should().Be("0021-08-01Z");
    }

    [Fact]
    public async Task SubtractNegativeYearMonthDuration_FromDateTime_StaysPositive()
    {
        // op-subtract-yearMonthDuration-from-dateTime-8
        var r = await _facade.EvaluateAsync("string(xs:dateTime(\"0001-01-01T01:01:01Z\") - xs:yearMonthDuration(\"-P20Y07M\"))");
        r.Should().Be("0021-08-01T01:01:01Z");
    }

    [Fact]
    public async Task AddDuration_AcrossYearOne_RoundTrips()
    {
        var r = await _facade.EvaluateAsync("string(xs:date(\"0000-12-20Z\") + xs:dayTimeDuration(\"P12D\"))");
        r.Should().Be("0001-01-01Z");
    }
}
