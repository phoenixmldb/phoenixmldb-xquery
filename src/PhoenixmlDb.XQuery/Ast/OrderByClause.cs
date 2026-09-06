using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Order by clause.
/// </summary>
public sealed class OrderByClause : FlworClause
{
    /// <summary>
    /// Whether this is a stable sort.
    /// </summary>
    public bool Stable { get; init; }

    public required IReadOnlyList<OrderSpec> OrderSpecs { get; init; }

    public override string ToString()
    {
        var stable = Stable ? "stable " : "";
        return $"{stable}order by {string.Join(", ", OrderSpecs)}";
    }
}
