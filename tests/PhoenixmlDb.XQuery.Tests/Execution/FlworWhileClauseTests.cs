using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Execution tests for the XQuery 4.0 <c>while</c> FLWOR clause. The clause terminates
/// tuple-stream iteration as soon as its condition first evaluates to <c>false</c>
/// (unlike <c>where</c>, which filters individual tuples but keeps iterating).
/// </summary>
public class FlworWhileClauseTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task WhileClause_stops_iteration_when_condition_becomes_false()
    {
        var results = await _facade.EvaluateAllAsync(
            "for $x in 1 to 10 while ($x < 5) return $x");

        // 1,2,3,4 pass; at $x = 5 the condition is false and iteration stops.
        results.Should().Equal("1", "2", "3", "4");
    }

    [Fact]
    public async Task WhileClause_true_condition_keeps_all_tuples()
    {
        var results = await _facade.EvaluateAllAsync(
            "for $x in 1 to 3 while (true()) return $x");

        results.Should().Equal("1", "2", "3");
    }

    [Fact]
    public async Task WhileClause_false_at_start_yields_nothing()
    {
        var results = await _facade.EvaluateAllAsync(
            "for $x in 1 to 10 while ($x > 100) return $x");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task WhileClause_does_not_resume_after_first_false()
    {
        // 6 fails immediately; even though 1..5 would pass < 6, the guard is $x != 3,
        // so iteration must stop at 3 and NOT resume for 4 or 5.
        var results = await _facade.EvaluateAllAsync(
            "for $x in (1, 2, 3, 4, 5) while ($x ne 3) return $x");

        results.Should().Equal("1", "2");
    }
}
