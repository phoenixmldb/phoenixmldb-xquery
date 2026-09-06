using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Tests node against a schema element declaration: <c>schema-element(Name)</c>.
/// Matches if the element has the declared name (or is in its substitution group)
/// and its type annotation is the declared type or a subtype.
/// Requires an <see cref="ISchemaProvider"/> to be registered.
/// </summary>
public sealed class SchemaElementTest : NodeTest
{
    /// <summary>The element declaration name from the schema.</summary>
    public required string LocalName { get; init; }

    /// <summary>Namespace prefix (for error messages).</summary>
    public string? Prefix { get; init; }

    /// <summary>Namespace URI (resolved from prefix).</summary>
    public string? NamespaceUri { get; init; }

    public override bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName)
    {
        // Runtime matching is handled by ISchemaProvider.MatchesSchemaElement();
        // this base method provides a structural name check only.
        return kind == XdmNodeKind.Element && localName == LocalName;
    }

    public override string ToString() => Prefix != null
        ? $"schema-element({Prefix}:{LocalName})"
        : $"schema-element({LocalName})";
}
