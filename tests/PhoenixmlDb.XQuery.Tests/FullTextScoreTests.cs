using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// phx:score returns the relevance a successful contains-text match recorded. Nothing recorded
/// one, so every score was 0, including for a node that had just matched, and ranking by score
/// was arbitrary (#71).
/// </summary>
public sealed class FullTextScoreTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task A_matched_node_scores_above_zero()
        => double.Parse(await _facade.EvaluateAsync(
                """let $b := <b>the quick brown fox</b> return ($b contains text "quick", phx:score($b))[2]"""),
            System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(1);

    [Fact]
    public async Task An_unmatched_node_scores_zero()
        => (await _facade.EvaluateAsync("""let $b := <b>slow</b> return ($b contains text "quick", phx:score($b))[2] = 0"""))
            .Should().Be("true");

    [Fact]
    public async Task The_score_survives_re_selecting_the_node()
        => double.Parse(await _facade.EvaluateAsync("""
                let $d := <a><body>quick fox</body></a>
                return ($d/body contains text "quick", phx:score($d/body))[2]
                """), System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(0);

    [Fact]
    public async Task Repetition_ranks_higher()
        => (await _facade.EvaluateAsync("""
                let $d := <d><t>quick</t><t>quick quick quick</t></d>
                return string-join(
                  for $t in $d/t where $t contains text "quick" order by phx:score($t) descending return string($t), '|')
                """)).Should().Be("quick quick quick|quick");

    [Fact]
    public async Task Covering_more_of_the_query_ranks_higher()
        => (await _facade.EvaluateAsync("""
                let $d := <d><t>quick</t><t>quick quick quick</t><t>quick brown fox</t></d>
                return string-join(
                  for $t in $d/t where $t contains text "quick fox" any word
                  order by phx:score($t) descending return string($t), '|')
                """)).Should().Be("quick brown fox|quick quick quick|quick");
}
