using System.Collections.Frozen;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// The built-in analyzer, with no dependency. In order: word boundaries by Unicode Standard Annex
/// #29 (Unicode 6.3 data); when stemming English, the possessive "'s" removed; lowercase unless
/// <c>using case sensitive</c>; stop words removed, leaving a gap in the positions; when
/// stemming English, the Porter stemmer. A word longer than 255 UTF-16 characters is not a token
/// but takes a position.
/// </summary>
/// <remarks>
/// It produces the same tokens, positions and offsets as the Lucene.Net 4.8 analysis chain that
/// XQuery 3.1 and earlier used (StandardTokenizer, EnglishPossessiveFilter, LowerCaseFilter,
/// StopFilter, PorterStemFilter), so an index built with the earlier release stays valid.
/// Stemming applies only when the language is absent or English ("en", "english", "en-…"); the
/// default stop words are the English list for every language.
/// </remarks>
public sealed class StandardFullTextAnalyzer : IFullTextAnalyzer
{
    /// <summary>The shared instance; the analyzer has no state.</summary>
    public static StandardFullTextAnalyzer Instance { get; } = new();

    /// <summary>The longest word, in UTF-16 characters, that is a token.</summary>
    public const int MaxTokenLength = 255;

    /// <summary>The default stop words: a short English list.</summary>
    public static IReadOnlySet<string> DefaultStopWords { get; } = new[]
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "if", "in", "into", "is",
        "it", "no", "not", "of", "on", "or", "such", "that", "the", "their", "then", "there",
        "these", "they", "this", "to", "was", "will", "with",
    }.ToFrozenSet(StringComparer.Ordinal);

    [ThreadStatic] private static PorterStemmer? t_stemmer;

    /// <inheritdoc/>
    public string Name => "phoenixml-standard";

    /// <inheritdoc/>
    public FtAnalyzerCapabilities Capabilities => FtAnalyzerCapabilities.None;

    /// <summary>Whether <paramref name="word"/>, lowercased, is a default stop word.</summary>
    public static bool IsDefaultStopWord(string word)
        => word.Length > 0 && DefaultStopWords.Contains(LowerCase(word.Trim()));

    /// <inheritdoc/>
    public IReadOnlyList<FtToken> Analyze(string text, FtAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(request);
        var language = request.Language?.ToLowerInvariant();
        var english = language is null or "en" or "english" || language.StartsWith("en-", StringComparison.Ordinal);
        var stem = request.Stemming && english;
        HashSet<string>? customStopWords = null;
        if (!request.NoStopWords && request.StopWords is { } custom)
        {
            // A custom list ignores case: a word and a token match when they lowercase alike.
            customStopWords = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in custom)
                customStopWords.Add(LowerCase(word));
        }

        var spans = new List<(int Start, int End)>();
        WordTokenizer.Tokenize(text, spans);
        var tokens = new List<FtToken>(spans.Count);
        var position = -1;
        var increment = 1;
        foreach (var (start, end) in spans)
        {
            if (end - start > MaxTokenLength)
            {
                increment++;
                continue;
            }
            var term = text[start..end];
            if (stem)
                term = WithoutPossessive(term);
            if (!request.CaseSensitive)
                term = LowerCase(term);
            if (!request.NoStopWords && (customStopWords is not null
                    ? customStopWords.Contains(LowerCase(term))
                    : DefaultStopWords.Contains(term)))
            {
                increment++;
                continue;
            }
            if (stem)
                term = (t_stemmer ??= new PorterStemmer()).Stem(term);
            if (request.FoldDiacritics)
                term = WithoutDiacritics(term);
            position += increment;
            increment = 1;
            tokens.Add(new FtToken(term, position, 0, 0, start, end));
        }
        return tokens;
    }

    /// <summary>The term without combining marks: decomposed, marks removed, recomposed.</summary>
    internal static string WithoutDiacritics(string term)
    {
        if (term.All(c => c < 0x80))
            return term;
        var decomposed = term.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>A trailing "'s" (apostrophe, right single quotation mark or fullwidth apostrophe; s either case) removed.</summary>
    private static string WithoutPossessive(string term)
    {
        if (term.Length >= 2 && term[^1] is 's' or 'S' && term[^2] is '\'' or '’' or '＇')
            return term[..^2];
        return term;
    }

    /// <summary>Each code point lowercased by the invariant culture; the UTF-16 length can change only for a supplementary character.</summary>
    internal static string LowerCase(string term)
    {
        var lower = false;
        foreach (var c in term)
        {
            if (char.ToLowerInvariant(c) != c || char.IsSurrogate(c))
            {
                lower = true;
                break;
            }
        }
        if (!lower)
            return term;
        var builder = new System.Text.StringBuilder(term.Length);
        for (var i = 0; i < term.Length; i++)
        {
            if (char.IsHighSurrogate(term[i]) && i + 1 < term.Length && char.IsLowSurrogate(term[i + 1]))
            {
                builder.Append(term.AsSpan(i, 2).ToString().ToLowerInvariant());
                i++;
            }
            else
            {
                builder.Append(char.ToLowerInvariant(term[i]));
            }
        }
        return builder.ToString();
    }
}
