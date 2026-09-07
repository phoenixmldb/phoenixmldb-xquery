namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// ftUnaryNot: ftnot selection
/// </summary>
public sealed class FtNotNode : FtSelectionNode
{
    public required FtSelectionNode Operand { get; init; }
}
