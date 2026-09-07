using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Group by clause (XQuery 3.0+).
/// </summary>
public sealed class GroupByClause : FlworClause
{
    public required IReadOnlyList<GroupingSpec> GroupingSpecs { get; init; }

    public override string ToString()
    {
        return $"group by {string.Join(", ", GroupingSpecs)}";
    }
}
