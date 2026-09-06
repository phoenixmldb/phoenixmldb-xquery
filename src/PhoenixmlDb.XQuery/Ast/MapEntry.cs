using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Entry in a map constructor.
/// </summary>
public sealed class MapEntry
{
    public required XQueryExpression Key { get; init; }
    public required XQueryExpression Value { get; init; }
}
