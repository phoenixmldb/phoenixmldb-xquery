using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>Insert a node relative to a target.</summary>
public sealed class InsertPrimitive : UpdatePrimitive
{
    public required object Source { get; init; }
    public required InsertPosition Position { get; init; }
}
