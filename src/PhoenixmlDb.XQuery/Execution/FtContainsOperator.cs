using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution.FullTextMatching;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Evaluates <c>source contains text selection (without content ignore)?</c> by the semantics of
/// XQuery and XPath Full Text 3.0 §4: the selection's AllMatches for each search context item,
/// true when one has a Match with no StringExclude (§4.3).
/// </summary>
public sealed class FtContainsOperator : PhysicalOperator
{
    public required PhysicalOperator Source { get; init; }
    public required FtSelectionNode Selection { get; init; }

    /// <summary>The nodes pruned from each search context item (<c>without content</c>).</summary>
    public PhysicalOperator? Ignore { get; init; }

    /// <summary>The module's <c>declare ft-option</c> match options.</summary>
    public FtMatchOptions? DefaultOptions { get; init; }

    /// <summary>The compiled form of each XQuery expression the selection embeds.</summary>
    public IReadOnlyDictionary<XQueryExpression, PhysicalOperator> EmbeddedOperators { get; init; }
        = new Dictionary<XQueryExpression, PhysicalOperator>();

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // §4.3 step 1: the embedded expressions and the ignore option are evaluated once, in the
        // dynamic context of the whole expression.
        var values = await FtEvaluatedExpressions.EvaluateAsync(EmbeddedOperators, context).ConfigureAwait(false);
        foreach (var weight in Selection.AllExpressions().Where(IsWeight))
            values.Weight(weight);   // FTDY0016 for a weight beyond 1000

        HashSet<object>? ignored = null;
        if (Ignore is not null)
        {
            ignored = [];
            await foreach (var item in Ignore.ExecuteGuarded(context).ConfigureAwait(false))
            {
                if (item is XdmNode node)
                    ignored.Add(FtSearchContext.Key(node));
                else if (item is not null)
                    throw new XQueryRuntimeException("XPTY0004", "The operand of without content must be a sequence of nodes");
            }
        }

        var options = FtEvaluator.StaticDefaults.With(DefaultOptions);
        FtEvaluator.CheckLanguage(options.Language, context.FullTextAnalyzers ?? FullText.FullTextAnalyzerRegistry.Default);
        var analyzers = context.FullTextAnalyzers ?? FullText.FullTextAnalyzerRegistry.Default;

        // Design 07 §5.4: a host's candidate source may rule out items that cannot match. It is
        // asked only for a selection that reduces to required terms; the evaluator still decides
        // every item it does not rule out.
        FullText.IFullTextCandidateFilter? candidates = null;
        if (context.FullTextCandidates is { } candidateSource && Ignore is null
            && FtCandidateTerms.Of(Selection, options, values, analyzers) is { } candidateQuery)
            candidates = candidateSource.Prepare(candidateQuery, context.Container);

        // Every item of the source is a search context of its own, and the expression is true if
        // ANY of them matches (§4.3).
        await foreach (var item in Source.ExecuteGuarded(context).ConfigureAwait(false))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (candidates is not null && !candidates.MayMatch(item))
                continue;
            if (FtSearchContext.Of(item, ignored, context) is not { } searchContext)
                continue;
            var matches = new FtEvaluator(searchContext, analyzers, values, context.FullTextResources, context.StaticBaseUri)
                .Evaluate(Selection, options, 0);
            if (matches.IsSatisfied)
            {
                // phx:score and score variables read what a successful match records here.
                context.SetFullTextScore(item, matches.Score());
                yield return true;
                yield break;
            }
        }
        yield return false;
    }

    private bool IsWeight(XQueryExpression expression)
        => Weights(Selection).Any(w => ReferenceEquals(w, expression));

    private static IEnumerable<XQueryExpression> Weights(FtSelectionNode node)
    {
        if (node is FtWithOptionsNode { Weight: { } weight })
            yield return weight;
        foreach (var child in node.Children)
            foreach (var w in Weights(child))
                yield return w;
    }
}
