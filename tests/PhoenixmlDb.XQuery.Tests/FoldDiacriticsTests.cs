using AwesomeAssertions;
using PhoenixmlDb.XQuery.FullText;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// contains text is diacritics insensitive by default (Full Text 3.0 §3.4.6), so an index whose
/// candidates must include every match folds diacritics too. FullTextAnalysisOptions had no way
/// to ask for it; the engine's index could not produce a superset for "café" / "cafe".
/// </summary>
public sealed class FoldDiacriticsTests
{
    [Fact]
    public void Folding_is_off_by_default_so_existing_tokens_do_not_change()
        => FullTextEngine.Analyze("Un café", new FullTextAnalysisOptions { Stemming = false }).Select(t => t.Text)
            .Should().Equal("un", "café");

    [Fact]
    public void Folding_removes_marks_after_stemming_and_lowercasing()
        => FullTextEngine.Analyze("Résumés naïve Ærøskøbing", new FullTextAnalysisOptions { Stemming = true, NoStopWords = true, FoldDiacritics = true })
            // Porter treats "é" as a consonant, so "résumés" stems to "résumé", then folds; the
            // evaluator compares the same fold(stem(lowercase)) key, so the two agree.
            .Select(t => t.Text).Should().Equal("resume", "naiv", "ærøskøbing");

    /// <summary>The index options the engine uses: every contains text match is among the index's candidates.</summary>
    [Theory]
    [InlineData("un café noir", "cafe")]
    [InlineData("un cafe noir", "café")]
    [InlineData("the walrus", "the")]
    [InlineData("they are running", "run")]
    public void The_index_options_give_a_superset(string text, string query)
    {
        var index = new FullTextAnalysisOptions { Stemming = true, NoStopWords = true, CaseSensitive = false, FoldDiacritics = true };
        var documentTerms = FullTextEngine.Analyze(text, index).Select(t => t.Text).ToHashSet();
        FullTextEngine.Analyze(query, index).Should().OnlyContain(t => documentTerms.Contains(t.Text));
    }
}
