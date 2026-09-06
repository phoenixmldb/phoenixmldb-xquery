using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Map constructor (XQuery 3.1): map { key: value, ... }.
/// </summary>
public sealed class MapConstructor : XQueryExpression
{
    public required IReadOnlyList<MapEntry> Entries { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitMapConstructor(this);

    public override string ToString()
    {
        var entries = string.Join(", ", Entries.Select(e => $"{e.Key}: {e.Value}"));
        return $"map {{ {entries} }}";
    }
}
