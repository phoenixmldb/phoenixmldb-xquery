using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.FullText;

namespace PhoenixmlDb.XQuery.Execution.FullTextMatching;

/// <summary>
/// fts:evaluate (Full Text 3.0 §4.2.3) for one search context item: the AllMatches of a
/// selection, with the match options propagated from the top down.
/// </summary>
internal sealed class FtEvaluator
{
    /// <summary>The match options in effect when a query sets none (Full Text 3.0 Appendix C).</summary>
    internal static readonly FtMatchOptions StaticDefaults = new()
    {
        Wildcards = false,
        Stemming = false,
        Case = FtCaseMode.Insensitive,
        DiacriticsSensitive = false,
        StopWords = new FtStopWordOption { None = true },
        Thesaurus = new FtThesaurusOption { None = true },
    };

    private readonly FtSearchContext _searchContext;
    private readonly FullTextAnalyzerRegistry _analyzers;
    private readonly FtEvaluatedExpressions _values;
    private readonly IFullTextResourceResolver? _resources;
    private readonly string? _baseUri;

    public FtEvaluator(FtSearchContext searchContext, FullTextAnalyzerRegistry analyzers, FtEvaluatedExpressions values,
        IFullTextResourceResolver? resources = null, string? baseUri = null)
    {
        _searchContext = searchContext;
        _analyzers = analyzers;
        _values = values;
        _resources = resources;
        _baseUri = baseUri;
    }

    /// <summary>A resource URI made absolute against the static base URI (§3.4.3, §3.4.7).</summary>
    private string Absolute(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out _) || _baseUri is null)
            return uri;
        return Uri.TryCreate(new Uri(_baseUri), uri, out var resolved) ? resolved.AbsoluteUri : uri;
    }

    public FtAllMatches Evaluate(FtSelectionNode node, FtMatchOptions options, int queryTokenNum)
    {
        switch (node)
        {
            case FtWithOptionsNode withOptions:
                // The weight changes only the score (§4.2.3).
                CheckLanguage(withOptions.Options?.Language, _analyzers);
                return Evaluate(withOptions.Selection, options.With(withOptions.Options), queryTokenNum);
            case FtExtensionSelectionNode extension:
                return Evaluate(extension.Selection!, options, queryTokenNum);
            case FtWordsNode words:
            {
                var matches = ApplyWords(words, options, queryTokenNum + 1);
                if (words.Times is { } times)
                {
                    var (kind, first, second) = _values.Range(times);
                    matches = FtAllMatches.Times(kind, first, second, matches);
                }
                return matches;
            }
            case FtAndNode and:
                return Fold(and.Operands, options, queryTokenNum, FtAllMatches.And);
            case FtOrNode or:
                return Fold(or.Operands, options, queryTokenNum, FtAllMatches.Or);
            case FtMildNotNode mildNot:
                return Fold(mildNot.Operands, options, queryTokenNum, FtAllMatches.MildNot);
            case FtNotNode not:
                return FtAllMatches.UnaryNot(Evaluate(not.Operand, options, queryTokenNum));
            case FtPositionFilterNode filter:
            {
                var nested = Evaluate(filter.Selection, options, queryTokenNum);
                switch (filter.Kind)
                {
                    case FtPositionFilterKind.Ordered:
                        return FtAllMatches.Order(nested);
                    case FtPositionFilterKind.Window:
                        return FtAllMatches.Window(filter.Unit, _values.Integer(filter.Size!), nested);
                    case FtPositionFilterKind.Distance:
                    {
                        var (kind, first, second) = _values.Range(filter.Range!);
                        return FtAllMatches.DistanceFilter(filter.Unit, kind, first, second, nested);
                    }
                    case FtPositionFilterKind.SameScope or FtPositionFilterKind.DifferentScope:
                        return FtAllMatches.Scope(filter.Kind == FtPositionFilterKind.SameScope, filter.Unit, nested);
                    default:
                    {
                        // The positions of the whole search context, under the options that tokenize it.
                        var tokens = Tokens(options);
                        return FtAllMatches.Content(filter.Kind, tokens.LowestPosition, tokens.HighestPosition, nested);
                    }
                }
            }
            default:
                throw new XQueryRuntimeException("XPST0003", $"Unsupported full-text selection {node.GetType().Name}");
        }
    }

    private FtAllMatches Fold(IReadOnlyList<FtSelectionNode> operands, FtMatchOptions options, int queryTokenNum,
        Func<FtAllMatches, FtAllMatches, FtAllMatches> combine)
    {
        var result = Evaluate(operands[0], options, queryTokenNum);
        for (var i = 1; i < operands.Count; i++)
            result = combine(result, Evaluate(operands[i], options, result.StokenNum));
        return result;
    }

    // ── FTWords (§4.2.4) ──────────────────────────────────────────────────────────────────────

    private FtAllMatches ApplyWords(FtWordsNode words, FtMatchOptions options, int queryPos)
    {
        var strings = words.Text is not null ? [words.Text] : _values.Strings(words.Expression!);
        var items = strings.Select(s => QueryTokens(s, options)).Where(q => q.Count > 0).ToList();
        if (items.Count == 0)
            return new FtAllMatches([], 0);
        switch (words.Mode)
        {
            case FtAnyAllOption.AnyWord:
            case FtAnyAllOption.AllWords:
            {
                var tokens = items.SelectMany(t => t).ToList();
                var result = Phrase([tokens[0]], options, queryPos);
                for (var i = 1; i < tokens.Count; i++)
                {
                    var next = Phrase([tokens[i]], options, queryPos + i);
                    result = words.Mode == FtAnyAllOption.AnyWord ? FtAllMatches.Or(result, next) : FtAllMatches.And(result, next);
                }
                return result;
            }
            case FtAnyAllOption.Phrase:
                return Phrase(Concatenate(items), options, queryPos);
            default:
            {
                // any / all: each string a phrase; disjunction / conjunction.
                var result = Phrase(items[0], options, queryPos);
                for (var i = 1; i < items.Count; i++)
                {
                    var nextPos = result.Matches.Count > 0 && result.Matches.Exists(m => m.Length > 0)
                        ? result.Matches.SelectMany(m => m).Max(s => s.QueryPos) + 1
                        : queryPos;
                    var next = Phrase(items[i], options, nextPos);
                    result = words.Mode == FtAnyAllOption.All ? FtAllMatches.And(result, next) : FtAllMatches.Or(result, next);
                }
                return result;
            }
        }
    }

    /// <summary>
    /// The tokens of several search strings as one sequence: each string's positions start at 0,
    /// so they are renumbered to follow the previous string's, keeping each string's own gaps.
    /// </summary>
    private static List<FtQueryToken> Concatenate(List<List<FtQueryToken>> items)
    {
        var result = new List<FtQueryToken>();
        var next = 0;
        foreach (var item in items)
        {
            var offset = next - item[0].Position;
            foreach (var token in item)
                result.Add(token with { Position = token.Position + offset });
            next = result[^1].Position + 1;
        }
        return result;
    }

    /// <summary>fts:applyQueryTokensAsPhrase (§4.2.5.2).</summary>
    private FtAllMatches Phrase(List<FtQueryToken> queryTokens, FtMatchOptions options, int queryPos)
    {
        var thesaurus = options.Thesaurus;
        if (thesaurus is { None: false })
        {
            // §4.2.5.2: the thesaurus gives alternatives for the whole phrase, matched as a
            // disjunction (FTWords any) with the thesaurus option off, so nothing expands twice.
            var phrase = string.Join(' ', queryTokens.Select(t => t.Raw));
            var alternatives = new List<string> { phrase };
            foreach (var id in thesaurus.Thesauri)
            {
                var source = id.Uri is null
                    ? _resources?.GetDefaultThesaurus(options.Language)
                    : _resources?.GetThesaurus(Absolute(id.Uri))
                      ?? throw new XQueryRuntimeException("FTST0018", $"Thesaurus \"{id.Uri}\" is not available");
                if (source is null)
                    continue;
                var (min, max) = id.Levels is { } levels
                    ? levels.Kind switch
                    {
                        FtRangeKind.Exactly => (levels.First, levels.First),
                        FtRangeKind.AtLeast => (levels.First, long.MaxValue),
                        FtRangeKind.AtMost => (1L, levels.First),
                        _ => (levels.First, levels.Second ?? levels.First),
                    }
                    : (1L, long.MaxValue);
                alternatives.AddRange(source.Expand(phrase, id.Relationship, Math.Max(1, min), max));
            }
            var withoutThesaurus = options.With(new FtMatchOptions { Thesaurus = new FtThesaurusOption { None = true } });
            FtAllMatches? any = null;
            foreach (var alternative in alternatives.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var alternativeTokens = QueryTokens(alternative, withoutThesaurus);
                if (alternativeTokens.Count == 0)
                    continue;
                var next = Phrase(alternativeTokens, withoutThesaurus, queryPos);
                any = any is null ? next : FtAllMatches.Or(any, next);
            }
            return any ?? new FtAllMatches([], queryPos);
        }

        var tokens = Tokens(options);
        var stopWords = StopWords(options);
        var result = new List<FtStringMatch[]>();
        var first = queryTokens[0].Position;
        foreach (var start in tokens.Tokens)
        {
            var startIndex = tokens.IndexOf(start.Position);
            var match = true;
            var endIndex = startIndex;
            foreach (var q in queryTokens)
            {
                var index = tokens.IndexOf(start.Position + (q.Position - first));
                if (index < 0 || !Matches(q, tokens, index, options, stopWords))
                {
                    match = false;
                    break;
                }
                endIndex = index;
            }
            if (!match)
                continue;
            var startToken = tokens.Tokens[startIndex];
            var endToken = tokens.Tokens[endIndex];
            result.Add([new FtStringMatch(queryPos, true, true, new FtTokenInfo(
                startToken.Position, endToken.Position,
                tokens.Sentences[startIndex], tokens.Sentences[endIndex],
                tokens.Paragraphs[startIndex], tokens.Paragraphs[endIndex]))]);
        }
        return new FtAllMatches(result, queryPos);
    }

    private bool Matches(FtQueryToken query, FtTokenizedText tokens, int index, FtMatchOptions options, HashSet<string> stopWords)
    {
        // A stop word in the query matches any one token (§3.4.7).
        if (stopWords.Contains(Fold(query.Raw.ToLowerInvariant(), false)))
            return true;
        var token = tokens.Tokens[index];
        if (options.Case is FtCaseMode.Lowercase or FtCaseMode.Uppercase)
        {
            var raw = _searchContext.Text[token.StartOffset..token.EndOffset];
            var required = options.Case == FtCaseMode.Lowercase ? raw.ToLowerInvariant() : raw.ToUpperInvariant();
            if (!string.Equals(raw, required, StringComparison.Ordinal))
                return false;
        }
        var sensitive = options.DiacriticsSensitive == true;
        if (query.Wildcard is { } pattern)
        {
            // Wildcards match the unstemmed token.
            var plain = Tokens(options, stemming: false);
            var plainIndex = plain.IndexOf(token.Position);
            return plainIndex >= 0 && pattern.IsMatch(Fold(plain.Tokens[plainIndex].Term, sensitive));
        }
        return string.Equals(Fold(token.Term, sensitive), Fold(query.Term, sensitive), StringComparison.Ordinal);
    }

    /// <summary>Diacritics insensitive: the text without combining marks (§3.4.6).</summary>
    internal static string Fold(string term, bool diacriticsSensitive)
        => diacriticsSensitive ? term : StandardFullTextAnalyzer.WithoutDiacritics(term);

    /// <summary>
    /// The language option (§3.4.1): FTST0013 when it is not an xs:language, FTST0009 when it
    /// names no language: a language is supported when an analyzer is registered for it or its
    /// primary subtag is an ISO 639 code (two or three letters), which the built-in analyzer
    /// tokenizes. The checks run once per language.
    /// </summary>
    internal static void CheckLanguage(string? language, FullTextAnalyzerRegistry analyzers)
    {
        if (language is null)
            return;
        if (!Regex.IsMatch(language, "^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$"))
            throw new XQueryRuntimeException("FTST0013", $"The language \"{language}\" is not a valid xs:language");
        var primary = language.Split('-')[0];
        if (!ReferenceEquals(analyzers.Resolve(language), analyzers.Fallback))
            return;
        if (primary.Length is < 2 or > 3)
            throw new XQueryRuntimeException("FTST0009", $"The language \"{language}\" is not supported");
    }

    private FtAnalysisRequest Request(FtMatchOptions options, bool? stemming = null) => new()
    {
        Language = options.Language,
        Stemming = stemming ?? options.Stemming == true,
        CaseSensitive = options.Case == FtCaseMode.Sensitive,
        NoStopWords = true,
    };

    private FtTokenizedText Tokens(FtMatchOptions options, bool? stemming = null)
        => _searchContext.Tokenize(_analyzers.Resolve(options.Language), Request(options, stemming));

    // ── Query tokens ──────────────────────────────────────────────────────────────────────────

    private List<FtQueryToken> QueryTokens(string query, FtMatchOptions options)
    {
        var analyzer = _analyzers.Resolve(options.Language);
        if (options.Wildcards != true)
        {
            return analyzer.Analyze(query, Request(options))
                .Select(t => new FtQueryToken(t.Term, query[t.StartOffset..t.EndOffset], t.Position, null))
                .ToList();
        }
        var result = new List<FtQueryToken>();
        var position = 0;
        foreach (var (text, pattern) in FtWildcards.Tokenize(query, options.Case == FtCaseMode.Sensitive, options.DiacriticsSensitive == true))
        {
            if (pattern is not null)
            {
                result.Add(new FtQueryToken(text, text, position++, pattern));
                continue;
            }
            foreach (var t in analyzer.Analyze(text, Request(options)))
                result.Add(new FtQueryToken(t.Term, text[t.StartOffset..t.EndOffset], position++, null));
        }
        return result;
    }

    /// <summary>The stop words in effect (§3.4.7, fts:applyStopWordOption), lowercased and folded.</summary>
    private HashSet<string> StopWords(FtMatchOptions options)
    {
        var option = options.StopWords;
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (option is null || option.None || option.Base is null)
            return set;
        set.UnionWith(Resolve(option.Base));
        foreach (var (union, list) in option.Operations)
        {
            if (union) set.UnionWith(Resolve(list));
            else set.ExceptWith(Resolve(list));
        }
        return set;

        IEnumerable<string> Resolve(FtStopWordList list)
        {
            IEnumerable<string> words;
            if (list.IsDefault)
                words = _resources?.GetDefaultStopWords(options.Language) ?? (IEnumerable<string>)StandardFullTextAnalyzer.DefaultStopWords;
            else if (list.Uri is not null)
                words = _resources?.GetStopWords(Absolute(list.Uri))
                        ?? throw new XQueryRuntimeException("FTST0008", $"Stop word list \"{list.Uri}\" is not available");
            else
                words = list.Words!;
            return words.Select(w => Fold(w.ToLowerInvariant(), false));
        }
    }
}

/// <summary>A query token: its analyzed term, its text as written, its position, and a wildcard pattern.</summary>
internal sealed record FtQueryToken(string Term, string Raw, int Position, Regex? Wildcard);
