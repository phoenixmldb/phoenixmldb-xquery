namespace PhoenixmlDb.XQuery.Ast;

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
