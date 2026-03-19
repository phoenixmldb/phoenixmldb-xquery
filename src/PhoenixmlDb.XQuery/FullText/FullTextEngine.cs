using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// Full-text search engine powered by Lucene.NET analyzers.
/// Handles tokenization, stemming, and text analysis for XQuery Full-Text queries.
///
/// Uses Lucene.NET for linguistic analysis (tokenizers, stemmers, stop words)
/// while keeping index storage separate (for LMDB integration).
/// </summary>
public sealed class FullTextEngine
{
    private const LuceneVersion MatchVersion = LuceneVersion.LUCENE_48;

    /// <summary>
    /// Analyzes text into a sequence of terms using the specified language and options.
    /// Returns terms with their positions for phrase/proximity matching.
    /// </summary>
    public static List<AnalyzedTerm> Analyze(string text, FullTextAnalysisOptions? options = null)
    {
        options ??= FullTextAnalysisOptions.Default;
        using var analyzer = GetAnalyzer(options);
        var terms = new List<AnalyzedTerm>();

        using var reader = new StringReader(text);
        using var tokenStream = analyzer.GetTokenStream("content", reader);

        var termAttr = tokenStream.AddAttribute<ICharTermAttribute>();
        var posAttr = tokenStream.AddAttribute<IPositionIncrementAttribute>();
        var offsetAttr = tokenStream.AddAttribute<IOffsetAttribute>();

        tokenStream.Reset();
        var position = -1;

        while (tokenStream.IncrementToken())
        {
            position += posAttr.PositionIncrement;
            terms.Add(new AnalyzedTerm
            {
                Text = termAttr.ToString(),
                Position = position,
                StartOffset = offsetAttr.StartOffset,
                EndOffset = offsetAttr.EndOffset
            });
        }

        tokenStream.End();
        return terms;
    }

    /// <summary>
    /// Checks if a text contains the specified search terms according to full-text semantics.
    /// This is the core evaluation for "contains text" expressions.
    /// </summary>
    public static bool ContainsText(
        string sourceText,
        string searchText,
        Ast.FtAnyAllOption mode,
        FullTextAnalysisOptions? options = null)
    {
        options ??= FullTextAnalysisOptions.Default;

        var sourceTerms = Analyze(sourceText, options);
        var searchTerms = Analyze(searchText, options);

        if (searchTerms.Count == 0) return true;
        if (sourceTerms.Count == 0) return false;

        var sourceTermSet = new HashSet<string>(sourceTerms.Select(t => t.Text));

        return mode switch
        {
            Ast.FtAnyAllOption.Any or Ast.FtAnyAllOption.AnyWord =>
                searchTerms.Any(st => sourceTermSet.Contains(st.Text)),

            Ast.FtAnyAllOption.All or Ast.FtAnyAllOption.AllWords =>
                searchTerms.All(st => sourceTermSet.Contains(st.Text)),

            Ast.FtAnyAllOption.Phrase =>
                ContainsPhrase(sourceTerms, searchTerms),

            _ => searchTerms.Any(st => sourceTermSet.Contains(st.Text))
        };
    }

    /// <summary>
    /// Checks if source contains search terms as a contiguous phrase.
    /// </summary>
    private static bool ContainsPhrase(List<AnalyzedTerm> source, List<AnalyzedTerm> search)
    {
        if (search.Count == 0) return true;
        if (search.Count > source.Count) return false;

        for (var i = 0; i <= source.Count - search.Count; i++)
        {
            var match = true;
            for (var j = 0; j < search.Count; j++)
            {
                if (source[i + j].Text != search[j].Text)
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if terms appear within a window of N positions.
    /// </summary>
    public static bool WithinWindow(List<AnalyzedTerm> source, List<AnalyzedTerm> search, int windowSize)
    {
        if (search.Count == 0) return true;

        // Find positions of each search term in the source
        var termPositions = new List<List<int>>();
        foreach (var st in search)
        {
            var positions = source
                .Where(s => s.Text == st.Text)
                .Select(s => s.Position)
                .ToList();
            if (positions.Count == 0) return false;
            termPositions.Add(positions);
        }

        // Check if there's a combination where all terms fit in a window
        return CheckWindowCombinations(termPositions, 0, [], windowSize);
    }

    private static bool CheckWindowCombinations(
        List<List<int>> termPositions, int index,
        List<int> current, int windowSize)
    {
        if (index == termPositions.Count)
        {
            var min = current.Min();
            var max = current.Max();
            return max - min < windowSize;
        }

        foreach (var pos in termPositions[index])
        {
            current.Add(pos);
            if (CheckWindowCombinations(termPositions, index + 1, current, windowSize))
                return true;
            current.RemoveAt(current.Count - 1);
        }

        return false;
    }

    /// <summary>
    /// Computes a BM25 relevance score for a document against a query.
    /// </summary>
    public static double ScoreBM25(
        List<AnalyzedTerm> documentTerms,
        List<AnalyzedTerm> queryTerms,
        int totalDocuments,
        int avgDocumentLength,
        Func<string, int>? documentFrequencyLookup = null)
    {
        const double k1 = 1.2;
        const double b = 0.75;

        var docLength = documentTerms.Count;
        var termFreqs = documentTerms.GroupBy(t => t.Text)
            .ToDictionary(g => g.Key, g => g.Count());

        double score = 0;
        foreach (var qt in queryTerms.Select(t => t.Text).Distinct())
        {
            var tf = termFreqs.GetValueOrDefault(qt, 0);
            if (tf == 0) continue;

            // IDF: log((N - df + 0.5) / (df + 0.5) + 1)
            var df = documentFrequencyLookup?.Invoke(qt) ?? 1;
            var idf = Math.Log((totalDocuments - df + 0.5) / (df + 0.5) + 1);

            // TF saturation
            var tfNorm = (tf * (k1 + 1)) / (tf + k1 * (1 - b + b * docLength / avgDocumentLength));

            score += idf * tfNorm;
        }

        return score;
    }

    /// <summary>
    /// Gets the appropriate Lucene analyzer for the given options.
    /// </summary>
    private static Analyzer GetAnalyzer(FullTextAnalysisOptions options)
    {
        // If stemming is disabled, use simple whitespace + lowercase
        if (options.Stemming == false)
        {
            return options.CaseSensitive == true
                ? new WhitespaceAnalyzer(MatchVersion)
                : new SimpleAnalyzer(MatchVersion);
        }

        // Language-specific analyzers with stemming
        return (options.Language?.ToLowerInvariant()) switch
        {
            "en" or "english" => new EnglishAnalyzer(MatchVersion),
            // For other languages, fall back to standard analyzer
            // Lucene.NET supports: de, fr, es, it, pt, nl, ru, etc.
            // Add specific analyzers as needed
            _ => new StandardAnalyzer(MatchVersion)
        };
    }
}

/// <summary>
/// A single analyzed term with position information.
/// </summary>
public sealed class AnalyzedTerm
{
    /// <summary>The normalized term text (lowercased, stemmed).</summary>
    public required string Text { get; init; }
    /// <summary>Position in the token stream (for phrase/proximity queries).</summary>
    public required int Position { get; init; }
    /// <summary>Start character offset in the original text.</summary>
    public int StartOffset { get; init; }
    /// <summary>End character offset in the original text.</summary>
    public int EndOffset { get; init; }
}

/// <summary>
/// Options controlling full-text analysis behavior.
/// </summary>
public sealed class FullTextAnalysisOptions
{
    public static FullTextAnalysisOptions Default { get; } = new();

    /// <summary>Enable stemming (e.g., "running" → "run"). Default: true.</summary>
    public bool? Stemming { get; init; } = true;
    /// <summary>Language for stemming/tokenization. Default: null (auto/standard).</summary>
    public string? Language { get; init; }
    /// <summary>Case-sensitive matching. Default: false (case-insensitive).</summary>
    public bool? CaseSensitive { get; init; }
    /// <summary>Enable wildcards in search terms. Default: false.</summary>
    public bool? Wildcards { get; init; }

    /// <summary>
    /// Creates options from XQuery Full-Text match options.
    /// </summary>
    public static FullTextAnalysisOptions FromFtMatchOptions(Ast.FtMatchOptions? ftOpts)
    {
        if (ftOpts == null) return Default;
        return new FullTextAnalysisOptions
        {
            Stemming = ftOpts.Stemming,
            Language = ftOpts.Language,
            CaseSensitive = ftOpts.CaseSensitive,
            Wildcards = ftOpts.Wildcards
        };
    }
}
