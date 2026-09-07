using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Window start/end condition.
/// </summary>
public sealed class WindowCondition
{
    public QName? CurrentItem { get; init; }
    public QName? PreviousItem { get; init; }
    public QName? NextItem { get; init; }
    public QName? Position { get; init; }
    public required XQueryExpression When { get; init; }
}
