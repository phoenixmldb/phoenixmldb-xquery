using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A single order specification.
/// </summary>
public sealed class OrderSpec
{
    public required XQueryExpression Expression { get; init; }
    public OrderDirection Direction { get; init; } = OrderDirection.Ascending;
    public EmptyOrder EmptyOrder { get; init; } = EmptyOrder.Least;
    public string? Collation { get; init; }

    public override string ToString()
    {
        var dir = Direction == OrderDirection.Descending ? " descending" : "";
        var empty = EmptyOrder == EmptyOrder.Greatest ? " empty greatest" : "";
        var coll = Collation != null ? $" collation \"{Collation}\"" : "";
        return $"{Expression}{dir}{empty}{coll}";
    }
}
