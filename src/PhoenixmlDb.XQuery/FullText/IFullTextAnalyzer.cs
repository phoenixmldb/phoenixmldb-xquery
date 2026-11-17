namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// Turns text into the tokens that full-text matching compares: tokenizing, stemming, case
/// folding and stop-word removal as one pipeline, because the XQuery and XPath Full Text 3.0
/// match options constrain their order. The evaluator (<c>contains text</c>) and a host's
/// full-text index use the same instance, so the two cannot disagree on what a token is.
/// </summary>
/// <remarks>
/// Register one per language on a <see cref="FullTextAnalyzerRegistry"/>. An analyzer must be
/// safe to call from several threads at once.
/// </remarks>
public interface IFullTextAnalyzer
{
    /// <summary>
    /// A stable name for what this analyzer produces. A host records it with an index; a
    /// different output under the same name means the index must be rebuilt.
    /// </summary>
    string Name { get; }

    /// <summary>What the tokens carry beyond term and position.</summary>
    FtAnalyzerCapabilities Capabilities { get; }

    /// <summary>Analyzes <paramref name="text"/> under the effective match options.</summary>
    IReadOnlyList<FtToken> Analyze(string text, FtAnalysisRequest request);
}

/// <summary>What an analyzer's tokens carry beyond term and position.</summary>
[Flags]
public enum FtAnalyzerCapabilities
{
    /// <summary>Terms and positions only.</summary>
    None = 0,
    /// <summary><see cref="FtToken.Sentence"/> is set (for <c>same sentence</c>).</summary>
    Sentences = 1,
    /// <summary><see cref="FtToken.Paragraph"/> is set (for <c>same paragraph</c>).</summary>
    Paragraphs = 2,
}

/// <summary>
/// One token. <see cref="Position"/> counts tokens and keeps the gap a removed stop word
/// leaves; <see cref="StartOffset"/> and <see cref="EndOffset"/> are UTF-16 offsets into the
/// analyzed text, end exclusive.
/// </summary>
public readonly record struct FtToken(
    string Term, int Position, int Sentence, int Paragraph, int StartOffset, int EndOffset);

/// <summary>The effective match options an analysis runs under, worked out by the evaluator.</summary>
public sealed record FtAnalysisRequest
{
    /// <summary>The request with every option at its default.</summary>
    public static FtAnalysisRequest Default { get; } = new();

    /// <summary>The language (<c>using language</c>); null when the query names none.</summary>
    public string? Language { get; init; }

    /// <summary>Stem tokens (<c>using stemming</c>). Default: true.</summary>
    public bool Stemming { get; init; } = true;

    /// <summary>Keep case (<c>using case sensitive</c>). Default: false.</summary>
    public bool CaseSensitive { get; init; }

    /// <summary>Wildcards are in effect (<c>using wildcards</c>).</summary>
    public bool Wildcards { get; init; }

    /// <summary>
    /// The stop words to remove, replacing the analyzer's default list
    /// (<c>using stop words (…)</c>); null for the default list unless <see cref="NoStopWords"/>.
    /// </summary>
    public IReadOnlyList<string>? StopWords { get; init; }

    /// <summary>Remove no stop words (<c>using no stop words</c>).</summary>
    public bool NoStopWords { get; init; }

    /// <summary>
    /// Remove diacritical marks from each term, after stemming and case folding
    /// (<c>using diacritics insensitive</c>). Default: false. An index that answers every
    /// <c>contains text</c> query with a superset of its matches folds them.
    /// </summary>
    public bool FoldDiacritics { get; init; }

    /// <summary>The request for the options of the existing <see cref="FullTextAnalysisOptions"/>.</summary>
    public static FtAnalysisRequest From(FullTextAnalysisOptions? options)
    {
        if (options is null || ReferenceEquals(options, FullTextAnalysisOptions.Default))
            return Default;
        return new FtAnalysisRequest
        {
            Language = options.Language,
            Stemming = options.Stemming != false,
            CaseSensitive = options.CaseSensitive == true,
            Wildcards = options.Wildcards == true,
            StopWords = options.StopWords,
            NoStopWords = options.NoStopWords,
            FoldDiacritics = options.FoldDiacritics,
        };
    }
}
