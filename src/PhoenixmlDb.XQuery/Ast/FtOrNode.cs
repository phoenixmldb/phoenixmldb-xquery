namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// ftOr: selection1 ftor selection2
/// </summary>
public sealed class FtOrNode : FtSelectionNode
{
    public required IReadOnlyList<FtSelectionNode> Operands { get; init; }
}
