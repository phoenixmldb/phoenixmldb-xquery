namespace PhoenixmlDb.XQuery.Ast;

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
