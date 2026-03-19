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

/// <summary>
/// Base class for full-text selection tree nodes.
/// </summary>
public abstract class FtSelectionNode { }

/// <summary>
/// ftOr: selection1 ftor selection2
/// </summary>
public sealed class FtOrNode : FtSelectionNode
{
    public required IReadOnlyList<FtSelectionNode> Operands { get; init; }
}

/// <summary>
/// ftAnd: selection1 ftand selection2
/// </summary>
public sealed class FtAndNode : FtSelectionNode
{
    public required IReadOnlyList<FtSelectionNode> Operands { get; init; }
}

/// <summary>
/// ftMildNot: selection1 not in selection2
/// </summary>
public sealed class FtMildNotNode : FtSelectionNode
{
    public required FtSelectionNode Include { get; init; }
    public required FtSelectionNode Exclude { get; init; }
}

/// <summary>
/// ftUnaryNot: ftnot selection
/// </summary>
public sealed class FtNotNode : FtSelectionNode
{
    public required FtSelectionNode Operand { get; init; }
}

/// <summary>
/// ftWords: literal or computed search text with any/all/phrase mode.
/// </summary>
public sealed class FtWordsNode : FtSelectionNode
{
    /// <summary>Literal search text (if known at compile time).</summary>
    public string? Text { get; init; }
    /// <summary>Computed search expression (if dynamic).</summary>
    public XQueryExpression? Expression { get; init; }
    /// <summary>Match mode: any word, all words, phrase, etc.</summary>
    public FtAnyAllOption Mode { get; init; } = FtAnyAllOption.Any;
}

/// <summary>
/// Full-text selection with position filters applied.
/// </summary>
public sealed class FtSelectionWithFilters : FtSelectionNode
{
    public required FtSelectionNode Selection { get; init; }
    public required IReadOnlyList<FtPositionFilter> PositionFilters { get; init; }
}

/// <summary>
/// Any/all match mode for full-text words.
/// </summary>
public enum FtAnyAllOption
{
    /// <summary>any — match if any word matches</summary>
    Any,
    /// <summary>any word — same as any</summary>
    AnyWord,
    /// <summary>all — match if all words match</summary>
    All,
    /// <summary>all words — same as all</summary>
    AllWords,
    /// <summary>phrase — match as exact phrase</summary>
    Phrase
}

/// <summary>
/// Position filter for full-text matches.
/// </summary>
public sealed class FtPositionFilter
{
    public required FtPositionFilterType Type { get; init; }
    public int Value { get; init; }
}

/// <summary>
/// Types of position filters.
/// </summary>
public enum FtPositionFilterType
{
    Ordered,
    Window,
    Distance,
    SameSentence,
    SameParagraph,
    AtStart,
    AtEnd,
    EntireContent
}

/// <summary>
/// Full-text match options (stemming, language, case, etc.).
/// </summary>
public sealed class FtMatchOptions
{
    /// <summary>Enable/disable stemming (null = default).</summary>
    public bool? Stemming { get; set; }
    /// <summary>Language for stemming/tokenization (e.g., "en", "de").</summary>
    public string? Language { get; set; }
    /// <summary>Enable/disable wildcards (null = default).</summary>
    public bool? Wildcards { get; set; }
    /// <summary>Case sensitivity (null = default/insensitive).</summary>
    public bool? CaseSensitive { get; set; }
    /// <summary>Diacritics sensitivity (null = default/insensitive).</summary>
    public bool? DiacriticsSensitive { get; set; }
    /// <summary>Custom stop words list.</summary>
    public IReadOnlyList<string>? StopWords { get; set; }
    /// <summary>Disable all stop words.</summary>
    public bool NoStopWords { get; set; }
    /// <summary>Thesaurus file path.</summary>
    public string? Thesaurus { get; set; }
}
