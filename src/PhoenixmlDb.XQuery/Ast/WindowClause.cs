using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Window clause for sliding/tumbling windows (XQuery 3.0+).
/// </summary>
public sealed class WindowClause : FlworClause
{
    public required WindowKind Kind { get; init; }
    public required QName Variable { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
    public required XQueryExpression Expression { get; init; }
    public required WindowCondition Start { get; init; }
    public WindowCondition? End { get; init; }
    /// <summary>True if the end condition uses "only end" — unclosed windows are not emitted.</summary>
    public bool OnlyEnd { get; init; }

    public override string ToString()
    {
        var kind = Kind == WindowKind.Tumbling ? "tumbling" : "sliding";
        return $"for {kind} window ${Variable.LocalName} in {Expression} start {Start}" +
               (End != null ? $" end {End}" : "");
    }
}
