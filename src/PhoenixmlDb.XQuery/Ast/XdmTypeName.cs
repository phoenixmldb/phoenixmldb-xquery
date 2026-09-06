using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Represents a type name (for schema-aware processing).
/// </summary>
public sealed class XdmTypeName
{
    public string? NamespaceUri { get; init; }
    public required string LocalName { get; init; }
    public string? Prefix { get; init; }

    public override string ToString()
    {
        return Prefix != null ? $"{Prefix}:{LocalName}" : LocalName;
    }
}
