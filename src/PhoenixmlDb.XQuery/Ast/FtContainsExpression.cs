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

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => default!;
}
