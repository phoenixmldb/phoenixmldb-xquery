using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A case in a switch expression.
/// </summary>
public sealed class SwitchCase
{
    public required IReadOnlyList<XQueryExpression> Values { get; init; }
    public required XQueryExpression Result { get; init; }

    public override string ToString()
    {
        var values = string.Join(" ", Values.Select(v => $"case {v}"));
        return $"{values} return {Result}";
    }
}
