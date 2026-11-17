using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.FullText;

namespace PhoenixmlDb.XQuery.Execution.FullTextMatching;

/// <summary>
/// The terms an item must hold to match a selection, for a candidate source (design 07 §5.4).
/// Conservative: where a selection cannot be reduced to required terms (ftnot, wildcards, a
/// thesaurus, occurs ranges that allow none, words that analyze to nothing), the answer is
/// "no requirement" and every item is evaluated.
/// </summary>
internal static class FtCandidateTerms
{
    private const int MaxAlternatives = 64;

    /// <summary>The query for <paramref name="selection"/>, or null when it requires nothing a source can use.</summary>
    public static FtCandidateQuery? Of(FtSelectionNode selection, FtMatchOptions options, FtEvaluatedExpressions values,
        FullTextAnalyzerRegistry analyzers)
    {
        var state = new State();
        var alternatives = Required(selection, options, values, analyzers, state);
        if (alternatives is null || state.MixedLanguages)
            return null;
        return new FtCandidateQuery(alternatives, state.Language, state.Stemming, !state.CaseInsensitive, !state.DiacriticsInsensitive);
    }

    private sealed class State
    {
        public bool Seen;
        public string? Language;
        public bool MixedLanguages;
        public bool Stemming;
        public bool CaseInsensitive;
        public bool DiacriticsInsensitive;

        // The flags describe the most permissive matching any primary uses, so a source that
        // serves them serves every primary.
        public void Note(FtMatchOptions options)
        {
            if (Seen && !string.Equals(Language, options.Language, StringComparison.OrdinalIgnoreCase))
                MixedLanguages = true;
            Language = options.Language;
            Seen = true;
            Stemming |= options.Stemming == true;
            CaseInsensitive |= options.Case != FtCaseMode.Sensitive;
            DiacriticsInsensitive |= options.DiacriticsSensitive != true;
        }
    }

    // Null: no requirement. Otherwise a disjunction of conjunctions of terms.
    private static List<List<string>>? Required(FtSelectionNode node, FtMatchOptions options, FtEvaluatedExpressions values,
        FullTextAnalyzerRegistry analyzers, State state)
    {
        switch (node)
        {
            case FtWithOptionsNode withOptions:
                return Required(withOptions.Selection, options.With(withOptions.Options), values, analyzers, state);
            case FtExtensionSelectionNode extension:
                return extension.Selection is null ? null : Required(extension.Selection, options, values, analyzers, state);
            case FtPositionFilterNode filter:
                // A filter only removes matches; what the selection requires still holds.
                return Required(filter.Selection, options, values, analyzers, state);
            case FtNotNode:
                return null;
            case FtMildNotNode mildNot:
                return Required(mildNot.Operands[0], options, values, analyzers, state);
            case FtOrNode or:
            {
                var result = new List<List<string>>();
                foreach (var operand in or.Operands)
                {
                    if (Required(operand, options, values, analyzers, state) is not { } alternatives)
                        return null;
                    result.AddRange(alternatives);
                }
                return result.Count > MaxAlternatives ? null : result;
            }
            case FtAndNode and:
            {
                var required = and.Operands
                    .Select(operand => Required(operand, options, values, analyzers, state))
                    .OfType<List<List<string>>>()   // an operand that requires nothing leaves the others
                    .ToList();
                if (required.Count == 0)
                    return null;
                var result = required[0];
                for (var i = 1; i < required.Count; i++)
                {
                    if (Product(result, required[i]) is not { } product)
                        return null;
                    result = product;
                }
                return result;
            }
            case FtWordsNode words:
                return Words(words, options, values, analyzers, state);
            default:
                return null;
        }
    }

    private static List<List<string>>? Product(List<List<string>> left, List<List<string>> right)
    {
        if ((long)left.Count * right.Count > MaxAlternatives)
            return null;
        return left.SelectMany(l => right.Select(r => l.Concat(r).Distinct(StringComparer.OrdinalIgnoreCase).ToList())).ToList();
    }

    private static List<List<string>>? Words(FtWordsNode words, FtMatchOptions options, FtEvaluatedExpressions values,
        FullTextAnalyzerRegistry analyzers, State state)
    {
        if (options.Wildcards == true || options.Thesaurus is { None: false })
            return null;
        if (words.Times is { } times && values.Range(times) is var (kind, first, _)
            && (kind == FtRangeKind.AtMost || (kind is FtRangeKind.Exactly or FtRangeKind.FromTo && first <= 0)))
            return null;   // "occurs at most N times" holds with no occurrence at all
        state.Note(options);

        // A stop word in the query matches any token, so with a stop word list in effect which
        // tokens are required is left to the evaluator.
        if (options.StopWords is { None: false })
            return null;
        // Tokens as written, for the source to analyze as its index does.
        var request = new FtAnalysisRequest { Language = options.Language, Stemming = false, CaseSensitive = true, NoStopWords = true };
        var analyzer = analyzers.Resolve(options.Language);
        var strings = words.Text is not null ? [words.Text] : values.Strings(words.Expression!);
        var perString = new List<List<string>>();
        foreach (var text in strings)
        {
            var tokens = analyzer.Analyze(text, request).Select(t => text[t.StartOffset..t.EndOffset]).ToList();
            if (tokens.Count == 0)
                continue;
            perString.Add(tokens);
        }
        if (perString.Count == 0)
            return null;
        return words.Mode switch
        {
            FtAnyAllOption.AnyWord => perString.SelectMany(t => t).Distinct(StringComparer.OrdinalIgnoreCase).Select(t => new List<string> { t }).ToList(),
            FtAnyAllOption.Any => perString,
            _ => [perString.SelectMany(t => t).Distinct(StringComparer.OrdinalIgnoreCase).ToList()],   // all, all words, phrase
        };
    }
}
