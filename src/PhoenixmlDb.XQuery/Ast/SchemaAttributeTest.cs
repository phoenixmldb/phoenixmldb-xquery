using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Tests node against a schema attribute declaration: <c>schema-attribute(Name)</c>.
/// Matches if the attribute has the declared name and its type annotation is
/// the declared type or a subtype.
/// Requires an <see cref="ISchemaProvider"/> to be registered.
/// </summary>
public sealed class SchemaAttributeTest : NodeTest
{
    /// <summary>The attribute declaration name from the schema.</summary>
    public required string LocalName { get; init; }

    /// <summary>Namespace prefix (for error messages).</summary>
    public string? Prefix { get; init; }

    /// <summary>Namespace URI (resolved from prefix).</summary>
    public string? NamespaceUri { get; init; }

    public override bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName)
    {
        // Runtime matching is handled by ISchemaProvider.MatchesSchemaAttribute();
        // this base method provides a structural name check only.
        return kind == XdmNodeKind.Attribute && localName == LocalName;
    }

    public override string ToString() => Prefix != null
        ? $"schema-attribute({Prefix}:{LocalName})"
        : $"schema-attribute({LocalName})";
}
