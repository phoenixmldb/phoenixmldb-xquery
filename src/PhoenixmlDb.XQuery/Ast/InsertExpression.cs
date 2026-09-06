using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// insert node(s) (before|after|as first into|as last into|into) target
/// </summary>
public sealed class InsertExpression : UpdateExpression
{
    /// <summary>The node(s) to insert.</summary>
    public required XQueryExpression Source { get; init; }
    /// <summary>The target node (parent or sibling).</summary>
    public required XQueryExpression Target { get; init; }
    /// <summary>Where to insert relative to the target.</summary>
    public required InsertPosition Position { get; init; }
}
