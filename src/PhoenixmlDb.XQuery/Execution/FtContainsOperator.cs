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

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate the source expression to get the text to search
        object? sourceValue = null;
        await foreach (var item in Source.ExecuteAsync(context))
            sourceValue = item;

        var sourceText = context.AtomizeWithNodes(sourceValue)?.ToString() ?? "";
        var options = FullText.FullTextAnalysisOptions.FromFtMatchOptions(MatchOptions);

        var result = EvaluateSelection(sourceText, Selection, options);
        yield return result;
    }

    private static bool EvaluateSelection(string text, Ast.FtSelectionNode selection, FullText.FullTextAnalysisOptions options)
    {
        return selection switch
        {
            Ast.FtWordsNode words => EvaluateWords(text, words, options),
            Ast.FtAndNode and => and.Operands.All(op => EvaluateSelection(text, op, options)),
            Ast.FtOrNode or => or.Operands.Any(op => EvaluateSelection(text, op, options)),
            Ast.FtNotNode not => !EvaluateSelection(text, not.Operand, options),
            Ast.FtMildNotNode mn => EvaluateSelection(text, mn.Include, options) && !EvaluateSelection(text, mn.Exclude, options),
            Ast.FtSelectionWithFilters filtered => EvaluateWithFilters(text, filtered, options),
            _ => false
        };
    }

    private static bool EvaluateWords(string text, Ast.FtWordsNode words, FullText.FullTextAnalysisOptions options)
    {
        var searchText = words.Text ?? "";
        if (string.IsNullOrEmpty(searchText)) return true;
        return FullText.FullTextEngine.ContainsText(text, searchText, words.Mode, options);
    }

    private static bool EvaluateWithFilters(string text, Ast.FtSelectionWithFilters filtered, FullText.FullTextAnalysisOptions options)
    {
        // First check if the basic selection matches
        if (!EvaluateSelection(text, filtered.Selection, options))
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
                        var searchTerms = FullText.FullTextEngine.Analyze(words.Text ?? "", options);
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
                        var srchTerms = FullText.FullTextEngine.Analyze(entireWords.Text ?? "", options);
                        if (srcTerms.Count != srchTerms.Count)
                            return false;
                    }
                    break;
            }
        }

        return true;
    }
}
