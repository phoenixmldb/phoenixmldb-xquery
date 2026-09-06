using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>Replace a node's value (text content).</summary>
public sealed class ReplaceValuePrimitive : UpdatePrimitive
{
    public required object Value { get; init; }
}
