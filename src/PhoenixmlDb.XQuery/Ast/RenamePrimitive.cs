using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>Rename a node.</summary>
public sealed class RenamePrimitive : UpdatePrimitive
{
    public required QName NewName { get; init; }
}
