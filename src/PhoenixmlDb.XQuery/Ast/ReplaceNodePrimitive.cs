using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>Replace a node with another.</summary>
public sealed class ReplaceNodePrimitive : UpdatePrimitive
{
    public required object Replacement { get; init; }
}
