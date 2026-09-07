using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Count clause (XQuery 3.0+).
/// </summary>
public sealed class CountClause : FlworClause
{
    public required QName Variable { get; init; }

    public override string ToString() => $"count ${Variable.LocalName}";
}
