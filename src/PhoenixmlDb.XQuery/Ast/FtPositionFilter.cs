namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Position filter for full-text matches.
/// </summary>
public sealed class FtPositionFilter
{
    public required FtPositionFilterType Type { get; init; }
    public int Value { get; init; }
}
