namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// ftMildNot: selection1 not in selection2
/// </summary>
public sealed class FtMildNotNode : FtSelectionNode
{
    public required FtSelectionNode Include { get; init; }
    public required FtSelectionNode Exclude { get; init; }
}
