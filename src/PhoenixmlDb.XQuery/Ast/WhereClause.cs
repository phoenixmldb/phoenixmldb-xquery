using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Where clause (where condition).
/// </summary>
public sealed class WhereClause : FlworClause
{
    public required XQueryExpression Condition { get; init; }

    public override string ToString() => $"where {Condition}";
}
