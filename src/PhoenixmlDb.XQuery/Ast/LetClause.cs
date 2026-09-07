using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Let clause (let $var := expr).
/// </summary>
public sealed class LetClause : FlworClause
{
    public required IReadOnlyList<LetBinding> Bindings { get; init; }

    public override string ToString()
    {
        return "let " + string.Join(", ", Bindings);
    }
}
