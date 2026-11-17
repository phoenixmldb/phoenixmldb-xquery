
namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// Full-text search engine.
/// Handles tokenization, stemming, and text analysis for XQuery Full-Text queries, through the
/// <see cref="IFullTextAnalyzer"/> a <see cref="FullTextAnalyzerRegistry"/> selects.
/// </summary>
public sealed class FullTextEngine
{
    /// <summary>
    /// Analyzes text into a sequence of terms using the specified language and options.
    /// Returns terms with their positions for phrase/proximity matching.
    /// </summary>
    public static List<AnalyzedTerm> Analyze(string text, FullTextAnalysisOptions? options = null)
        => Analyze(text, options, null);

    /// <summary>
    /// Analyzes text with the analyzer <paramref name="analyzers"/> registers for the language
    /// of <paramref name="options"/>; with no registry, the built-in
    /// <see cref="StandardFullTextAnalyzer"/>.
    /// </summary>
    public static List<AnalyzedTerm> Analyze(string text, FullTextAnalysisOptions? options, FullTextAnalyzerRegistry? analyzers)
    {
        var request = FtAnalysisRequest.From(options);
        var analyzer = (analyzers ?? options?.Analyzers ?? FullTextAnalyzerRegistry.Default).Resolve(request.Language);
        var tokens = analyzer.Analyze(text, request);
        var terms = new List<AnalyzedTerm>(tokens.Count);
        foreach (var token in tokens)
        {
            terms.Add(new AnalyzedTerm
            {
                Text = token.Term,
                Position = token.Position,
                StartOffset = token.StartOffset,
                EndOffset = token.EndOffset,
            });
        }
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

        // XQuery and XPath Full Text 3.0 §3.2.1: under `any` and `all` — `any` is the default —
        // EACH search string is a phrase, its tokens consecutive in the source; `any word` and
        // `all words` are the token-level modes. `any`/`all` were treated as token-level, so
        // "walrus tusk" contains text "tusk walrus" was true. For ONE search string, `any`,
        // `all` and `phrase` all mean "this string as a phrase"; they differ only in how
        // several strings combine, which the caller decides (FtContainsOperator.EvaluateWords).
        return mode switch
        {
            Ast.FtAnyAllOption.AnyWord =>
                searchTerms.Any(st => sourceTermSet.Contains(st.Text)),

            Ast.FtAnyAllOption.AllWords =>
                searchTerms.All(st => sourceTermSet.Contains(st.Text)),

            Ast.FtAnyAllOption.Any or Ast.FtAnyAllOption.All or Ast.FtAnyAllOption.Phrase =>
                ContainsPhrase(sourceTerms, searchTerms),

            _ => ContainsPhrase(sourceTerms, searchTerms)
        };
    }

    /// <summary>
    /// Checks if source contains the search terms as a phrase: in order, at the same POSITION
    /// offsets as in the search text.
    /// </summary>
    /// <remarks>
    /// Positions carry the gaps a removed stop word leaves. A stop word in the search phrase
    /// therefore stands for any one token (XQuery Full Text §3.4.7), and a phrase does not match
    /// across a gap it does not have: "walrus carpenter" does not match "the walrus and the
    /// carpenter". Matching compared LIST indices, so it did (#30), and a position-aware index
    /// disagreed with the evaluator.
    /// </remarks>
    private static bool ContainsPhrase(List<AnalyzedTerm> source, List<AnalyzedTerm> search)
    {
        if (search.Count == 0) return true;
        if (search.Count > source.Count) return false;

        var byPosition = new Dictionary<int, string>(source.Count);
        foreach (var term in source)
            byPosition.TryAdd(term.Position, term.Text);
        var first = search[0];
        foreach (var start in source)
        {
            if (start.Text != first.Text) continue;
            var match = true;
            for (var j = 1; j < search.Count; j++)
            {
                var at = start.Position + (search[j].Position - first.Position);
                if (!byPosition.TryGetValue(at, out var text) || text != search[j].Text)
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
    /// Whether <paramref name="word"/> is in the default stop-word list
    /// (<see cref="StandardFullTextAnalyzer.DefaultStopWords"/>).
    /// </summary>
    public static bool IsDefaultStopWord(string word) => StandardFullTextAnalyzer.IsDefaultStopWord(word);
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
    /// The stop words to remove, replacing the default English list (<c>using stop words (…)</c>).
    /// Null: the default list, unless <see cref="NoStopWords"/>.
    /// </summary>
    public IReadOnlyList<string>? StopWords { get; init; }
    /// <summary>Remove no stop words at all (<c>using no stop words</c>). Default: false.</summary>
    public bool NoStopWords { get; init; }

    /// <summary>
    /// The analyzers to choose from by <see cref="Language"/>; null for the built-in
    /// <see cref="StandardFullTextAnalyzer"/>. Not a match option: it says who analyzes.
    /// </summary>
    public FullTextAnalyzerRegistry? Analyzers { get; init; }

    /// <summary>These options, analyzed by <paramref name="analyzers"/>.</summary>
    public FullTextAnalysisOptions WithAnalyzers(FullTextAnalyzerRegistry? analyzers)
        => ReferenceEquals(analyzers, Analyzers) ? this : new FullTextAnalysisOptions
        {
            Stemming = Stemming,
            Language = Language,
            CaseSensitive = CaseSensitive,
            Wildcards = Wildcards,
            StopWords = StopWords,
            NoStopWords = NoStopWords,
            Analyzers = analyzers,
        };

    /// <summary>
    /// Creates options from XQuery Full-Text match options.
    /// </summary>
    public static FullTextAnalysisOptions FromFtMatchOptions(Ast.FtMatchOptions? ftOpts)
    {
        if (ftOpts == null) return Default;
        return new FullTextAnalysisOptions
        {
            // Unspecified stemming keeps the default rather than becoming "off".
            Stemming = ftOpts.Stemming ?? Default.Stemming,
            Language = ftOpts.Language,
            CaseSensitive = ftOpts.CaseSensitive,
            Wildcards = ftOpts.Wildcards,
            // Only a literal list maps onto these options; lists by URI and union/except are
            // resolved by the contains text evaluator.
            StopWords = ftOpts.StopWords is { None: false, Base.Words: { } words, Operations.Count: 0 } ? words : null,
            NoStopWords = ftOpts.StopWords is { None: true },
        };
    }
}
