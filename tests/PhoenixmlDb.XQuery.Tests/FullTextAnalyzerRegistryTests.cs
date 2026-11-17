using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.FullText;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Design 07 §5.1: the evaluator and a host's index take the same analyzer, registered per
/// language with a fallback. A registered analyzer must reach <c>contains text</c> and the
/// <c>phx:</c> full-text functions, and the built-in one must stay the default.
/// </summary>
public sealed class FullTextAnalyzerRegistryTests
{
    /// <summary>Splits on spaces and reverses each word, so its tokens cannot be the built-in ones.</summary>
    private sealed class ReversingAnalyzer : IFullTextAnalyzer
    {
        public string Name => "test-reversing";
        public FtAnalyzerCapabilities Capabilities => FtAnalyzerCapabilities.None;

        public IReadOnlyList<FtToken> Analyze(string text, FtAnalysisRequest request)
        {
            var tokens = new List<FtToken>();
            var position = 0;
            var offset = 0;
            foreach (var word in text.Split(' '))
            {
                if (word.Length > 0)
                    tokens.Add(new FtToken(new string(word.Reverse().ToArray()), position++, 0, 0, offset, offset + word.Length));
                offset += word.Length + 1;
            }
            return tokens;
        }
    }

    private static async Task<List<object?>> EvalAsync(string query, FullTextAnalyzerRegistry? analyzers)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env) { FullTextAnalyzers = analyzers };
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return items;
    }

    [Fact]
    public void A_language_resolves_exactly_then_by_primary_subtag_then_to_the_fallback()
    {
        var german = new ReversingAnalyzer();
        var registry = new FullTextAnalyzerRegistry().Register("de", german);
        registry.Resolve("de").Should().BeSameAs(german);
        registry.Resolve("DE-at").Should().BeSameAs(german);
        registry.Resolve("fr").Should().BeSameAs(StandardFullTextAnalyzer.Instance);
        registry.Resolve(null).Should().BeSameAs(StandardFullTextAnalyzer.Instance);
    }

    [Fact]
    public async Task Contains_text_uses_the_analyzer_registered_for_its_language()
    {
        var registry = new FullTextAnalyzerRegistry().Register("de", new ReversingAnalyzer());
        // Stemming makes "walruses" match "walrus" in English; the German analyzer does not stem.
        (await EvalAsync("""("walruses" contains text "walrus" using stemming using language "en", "walruses" contains text "walrus" using stemming using language "de")""", registry))
            .Should().Equal(true, false);
    }

    [Fact]
    public async Task The_fallback_analyzer_serves_a_query_with_no_language()
    {
        var registry = new FullTextAnalyzerRegistry(new ReversingAnalyzer());
        (await EvalAsync("""("walruses" contains text "walrus", phx:tokenize("ab cd"))""", registry))
            .Should().Equal(false, "ba", "dc");
    }

    [Fact]
    public async Task Without_a_registry_the_built_in_analyzer_is_used()
        => (await EvalAsync("""("walruses" contains text "walrus" using stemming, phx:tokenize("The Walruses"))""", null))
            .Should().Equal(true, "walrus");

    [Fact]
    public void Default_stop_words_are_the_short_English_list()
    {
        StandardFullTextAnalyzer.IsDefaultStopWord(" The ").Should().BeTrue();
        StandardFullTextAnalyzer.IsDefaultStopWord("walrus").Should().BeFalse();
        StandardFullTextAnalyzer.DefaultStopWords.Should().HaveCount(33);
    }
}
