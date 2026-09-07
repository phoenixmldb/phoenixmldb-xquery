namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Full-text selection with position filters applied.
/// </summary>
public sealed class FtSelectionWithFilters : FtSelectionNode
{
    public required FtSelectionNode Selection { get; init; }
    public required IReadOnlyList<FtPositionFilter> PositionFilters { get; init; }
}
