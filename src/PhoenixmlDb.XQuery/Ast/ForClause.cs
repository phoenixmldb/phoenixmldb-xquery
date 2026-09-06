using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// For clause (for $var in expr).
/// </summary>
public sealed class ForClause : FlworClause
{
    public required IReadOnlyList<ForBinding> Bindings { get; init; }
    /// <summary>
    /// True for "for member" (XPath 4.0) — iterates over array members instead of sequence items.
    /// </summary>
    public bool IsMember { get; init; }

    public override string ToString()
    {
        return (IsMember ? "for member " : "for ") + string.Join(", ", Bindings);
    }
}
