using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A catch clause in a try-catch expression.
/// </summary>
public sealed class CatchClause
{
    public required IReadOnlyList<NameTest> ErrorCodes { get; init; }
    public required XQueryExpression Expression { get; init; }

    public override string ToString()
    {
        var codes = ErrorCodes.Count > 0
            ? string.Join(" | ", ErrorCodes)
            : "*";
        return $"catch {codes} {{ {Expression} }}";
    }
}
