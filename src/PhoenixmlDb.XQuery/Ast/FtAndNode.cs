namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// ftAnd: selection1 ftand selection2
/// </summary>
public sealed class FtAndNode : FtSelectionNode
{
    public required IReadOnlyList<FtSelectionNode> Operands { get; init; }
}
