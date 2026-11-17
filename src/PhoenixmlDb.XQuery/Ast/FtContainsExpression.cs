namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// <c>source contains text selection (without content ignore)?</c> (Full Text 3.0 [88]).
/// </summary>
public sealed class FtContainsExpression : XQueryExpression
{
    /// <summary>The search context: each item is searched, and any match makes the expression true.</summary>
    public required XQueryExpression Source { get; init; }
    /// <summary>The full-text selection (what to search for).</summary>
    public required FtSelectionNode Selection { get; init; }
    /// <summary>The nodes pruned from each search context item before matching (<c>without content</c>, [235]).</summary>
    public XQueryExpression? Ignore { get; init; }
    /// <summary>
    /// The default match options of the module (<c>declare ft-option</c>, [26]), which the
    /// selection's own options override.
    /// </summary>
    public FtMatchOptions? DefaultOptions { get; init; }

    // This returned default! without dispatching. Every analysis pass that REWRITES the tree
    // replaces a node with what Accept returns, so the namespace resolver — the first pass —
    // turned every `contains text` expression into null, and the next pass dereferenced it:
    // a NullReferenceException at compile time for every full-text query (xquery#13). QT3 has
    // no full-text coverage, so no conformance run ever built one.
    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitFtContains(this);

    /// <summary>The expressions embedded in a selection, e.g. <c>contains text {$terms}</c>.</summary>
    internal static IEnumerable<XQueryExpression> EmbeddedExpressions(FtSelectionNode node) => node.AllExpressions();

    /// <summary>The selection with each embedded expression passed through <paramref name="map"/>.</summary>
    internal static FtSelectionNode MapExpressions(FtSelectionNode node, Func<XQueryExpression, XQueryExpression> map)
        => node.Map(map);
}
