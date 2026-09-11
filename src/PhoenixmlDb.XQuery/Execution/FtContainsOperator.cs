using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Evaluates a "contains text" expression using the Lucene.NET-powered full-text engine.
/// </summary>
public sealed class FtContainsOperator : PhysicalOperator
{
    public required PhysicalOperator Source { get; init; }
    public required Ast.FtSelectionNode Selection { get; init; }
    public Ast.FtMatchOptions? MatchOptions { get; init; }

    /// <summary>The compiled expression of each FTWords leaf written as <c>{expr}</c>.</summary>
    public IReadOnlyDictionary<Ast.FtWordsNode, PhysicalOperator>? WordOperators { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var options = FullText.FullTextAnalysisOptions.FromFtMatchOptions(MatchOptions);

        // Search words written as {expr} are evaluated once: they do not depend on the item
        // being searched.
        Dictionary<Ast.FtWordsNode, List<string>>? words = null;
        if (WordOperators is { Count: > 0 })
        {
            words = [];
            foreach (var (node, op) in WordOperators)
            {
                var strings = new List<string>();
                await foreach (var item in op.ExecuteAsync(context))
                    strings.Add(context.AtomizeWithNodes(item)?.ToString() ?? "");
                words[node] = strings;
            }
        }

        // Every item of the source is a search context of its own, and the expression is true
        // if ANY of them matches (XQuery Full Text 3.0 §3.1). This kept only the LAST item —
        // `("seal", "walrus") contains text "seal"` was false.
        await foreach (var item in Source.ExecuteAsync(context))
        {
            var sourceText = context.AtomizeWithNodes(item)?.ToString() ?? "";
            if (EvaluateSelection(sourceText, Selection, options, words))
            {
                yield return true;
                yield break;
            }
        }
        yield return false;
    }

    private static bool EvaluateSelection(string text, Ast.FtSelectionNode selection,
        FullText.FullTextAnalysisOptions options, Dictionary<Ast.FtWordsNode, List<string>>? words)
    {
        return selection switch
        {
            Ast.FtWordsNode w => EvaluateWords(text, w, options, words),
            Ast.FtAndNode and => and.Operands.All(op => EvaluateSelection(text, op, options, words)),
            Ast.FtOrNode or => or.Operands.Any(op => EvaluateSelection(text, op, options, words)),
            Ast.FtNotNode not => !EvaluateSelection(text, not.Operand, options, words),
            Ast.FtMildNotNode mn => EvaluateSelection(text, mn.Include, options, words)
                && !EvaluateSelection(text, mn.Exclude, options, words),
            Ast.FtSelectionWithFilters filtered => EvaluateWithFilters(text, filtered, options, words),
            _ => false
        };
    }

    /// <summary>
    /// One FTWords leaf. A leaf with no search tokens matches NOTHING (§3.2): it returned true,
    /// which is what made every <c>{expr}</c> leaf — read as empty literal text — match every
    /// document. Several strings from an expression combine by the leaf's mode: any of them,
    /// all of them, or together as one phrase.
    /// </summary>
    private static bool EvaluateWords(string text, Ast.FtWordsNode words,
        FullText.FullTextAnalysisOptions options, Dictionary<Ast.FtWordsNode, List<string>>? values)
    {
        var searches = SearchStringsOf(words, values)
            .Where(s => FullText.FullTextEngine.Analyze(s, options).Count > 0)
            .ToList();
        if (searches.Count == 0)
            return false;
        // How several search strings combine (§3.2.1): `any` — some string matches as a phrase;
        // `all` — every string does; `phrase` — all of them, in order, as ONE phrase; `any word`
        // — some token of any string; `all words` — every token of every string.
        return words.Mode switch
        {
            Ast.FtAnyAllOption.All =>
                searches.All(s => FullText.FullTextEngine.ContainsText(text, s, words.Mode, options)),
            Ast.FtAnyAllOption.Phrase or Ast.FtAnyAllOption.AnyWord or Ast.FtAnyAllOption.AllWords =>
                FullText.FullTextEngine.ContainsText(text, string.Join(' ', searches), words.Mode, options),
            _ => searches.Any(s => FullText.FullTextEngine.ContainsText(text, s, words.Mode, options)),
        };
    }

    private static IEnumerable<string> SearchStringsOf(Ast.FtWordsNode words, Dictionary<Ast.FtWordsNode, List<string>>? values)
        => values != null && values.TryGetValue(words, out var strings) ? strings : [words.Text ?? ""];

    private static bool EvaluateWithFilters(string text, Ast.FtSelectionWithFilters filtered,
        FullText.FullTextAnalysisOptions options, Dictionary<Ast.FtWordsNode, List<string>>? values)
    {
        // First check if the basic selection matches
        if (!EvaluateSelection(text, filtered.Selection, options, values))
            return false;

        // Apply position filters
        foreach (var filter in filtered.PositionFilters)
        {
            switch (filter.Type)
            {
                case Ast.FtPositionFilterType.Window:
                    // Check if matched terms are within N positions
                    if (filtered.Selection is Ast.FtWordsNode words)
                    {
                        var sourceTerms = FullText.FullTextEngine.Analyze(text, options);
                        var searchTerms = FullText.FullTextEngine.Analyze(string.Join(' ', SearchStringsOf(words, values)), options);
                        if (!FullText.FullTextEngine.WithinWindow(sourceTerms, searchTerms, filter.Value))
                            return false;
                    }
                    break;

                case Ast.FtPositionFilterType.Ordered:
                    // Check terms appear in order — for phrase this is already guaranteed
                    break;

                case Ast.FtPositionFilterType.EntireContent:
                    // Check that the match covers the entire content
                    if (filtered.Selection is Ast.FtWordsNode entireWords)
                    {
                        var srcTerms = FullText.FullTextEngine.Analyze(text, options);
                        var srchTerms = FullText.FullTextEngine.Analyze(string.Join(' ', SearchStringsOf(entireWords, values)), options);
                        if (srcTerms.Count != srchTerms.Count)
                            return false;
                    }
                    break;
            }
        }

        return true;
    }
}
