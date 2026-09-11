namespace PhoenixmlDb.XQuery.Ast;


// ═══════════════════════════════════════════════════════════════════════════
// XQuery and XPath Full Text 3.0 — AST node definitions
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// expr contains text ftSelection (using matchOptions)?
/// The top-level full-text contains expression.
/// </summary>
public sealed class FtContainsExpression : XQueryExpression
{
    /// <summary>The source expression whose string value is searched.</summary>
    public required XQueryExpression Source { get; init; }
    /// <summary>The full-text selection (what to search for).</summary>
    public required FtSelectionNode Selection { get; init; }
    /// <summary>Optional match options (stemming, language, etc.).</summary>
    public FtMatchOptions? MatchOptions { get; init; }

    // This returned default! without dispatching. Every analysis pass that REWRITES the tree
    // replaces a node with what Accept returns, so the namespace resolver — the first pass —
    // turned every `contains text` expression into null, and the next pass dereferenced it:
    // a NullReferenceException at compile time for every full-text query (xquery#13). QT3 has
    // no full-text coverage, so no conformance run ever built one.
    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitFtContains(this);

    /// <summary>The expressions embedded in a selection, e.g. <c>contains text {$terms}</c>.</summary>
    internal static IEnumerable<XQueryExpression> EmbeddedExpressions(FtSelectionNode node)
        => WordsNodes(node).Where(w => w.Expression is not null).Select(w => w.Expression!);

    /// <summary>Every FTWords leaf of a selection, in document order.</summary>
    internal static IEnumerable<FtWordsNode> WordsNodes(FtSelectionNode node)
    {
        switch (node)
        {
            case FtWordsNode words:
                yield return words;
                break;
            case FtOrNode or:
                foreach (var op in or.Operands)
                    foreach (var w in WordsNodes(op)) yield return w;
                break;
            case FtAndNode and:
                foreach (var op in and.Operands)
                    foreach (var w in WordsNodes(op)) yield return w;
                break;
            case FtMildNotNode mild:
                foreach (var w in WordsNodes(mild.Include)) yield return w;
                foreach (var w in WordsNodes(mild.Exclude)) yield return w;
                break;
            case FtNotNode not:
                foreach (var w in WordsNodes(not.Operand)) yield return w;
                break;
            case FtSelectionWithFilters filtered:
                foreach (var w in WordsNodes(filtered.Selection)) yield return w;
                break;
        }
    }

    /// <summary>
    /// The selection with each embedded expression passed through <paramref name="map"/>; the
    /// same instance when nothing changed, so a rewriter that changes nothing allocates nothing.
    /// </summary>
    internal static FtSelectionNode MapExpressions(FtSelectionNode node, Func<XQueryExpression, XQueryExpression> map)
    {
        switch (node)
        {
            case FtWordsNode { Expression: { } e } words:
                var mapped = map(e);
                return ReferenceEquals(mapped, e)
                    ? words
                    : new FtWordsNode { Text = words.Text, Expression = mapped, Mode = words.Mode };
            case FtOrNode or:
                return MapList(or.Operands, map) is { } orOps ? new FtOrNode { Operands = orOps } : or;
            case FtAndNode and:
                return MapList(and.Operands, map) is { } andOps ? new FtAndNode { Operands = andOps } : and;
            case FtMildNotNode mild:
            {
                var include = MapExpressions(mild.Include, map);
                var exclude = MapExpressions(mild.Exclude, map);
                return ReferenceEquals(include, mild.Include) && ReferenceEquals(exclude, mild.Exclude)
                    ? mild
                    : new FtMildNotNode { Include = include, Exclude = exclude };
            }
            case FtNotNode not:
            {
                var operand = MapExpressions(not.Operand, map);
                return ReferenceEquals(operand, not.Operand) ? not : new FtNotNode { Operand = operand };
            }
            case FtSelectionWithFilters filtered:
            {
                var inner = MapExpressions(filtered.Selection, map);
                return ReferenceEquals(inner, filtered.Selection)
                    ? filtered
                    : new FtSelectionWithFilters { Selection = inner, PositionFilters = filtered.PositionFilters };
            }
            default:
                return node;
        }
    }

    // Null when no operand changed.
    private static List<FtSelectionNode>? MapList(IReadOnlyList<FtSelectionNode> operands, Func<XQueryExpression, XQueryExpression> map)
    {
        List<FtSelectionNode>? result = null;
        for (var i = 0; i < operands.Count; i++)
        {
            var mapped = MapExpressions(operands[i], map);
            if (result is null && !ReferenceEquals(mapped, operands[i]))
            {
                result = new List<FtSelectionNode>(operands.Count);
                for (var j = 0; j < i; j++) result.Add(operands[j]);
            }
            result?.Add(mapped);
        }
        return result;
    }
}
