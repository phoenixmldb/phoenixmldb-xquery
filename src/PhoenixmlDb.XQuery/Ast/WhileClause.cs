using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// While clause (XQuery 4.0) — terminates FLWOR iteration when condition becomes false.
/// </summary>
public sealed class WhileClause : FlworClause
{
    public required XQueryExpression Condition { get; init; }

    public override string ToString() => $"while ({Condition})";
}
