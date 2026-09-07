namespace PhoenixmlDb.XQuery.Ast;

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
