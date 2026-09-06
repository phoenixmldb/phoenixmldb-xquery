using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Schema import declaration: <c>import schema [namespace prefix =] "uri" [at "hint"]</c>.
/// Imports type definitions and element/attribute declarations from an XSD schema.
/// Requires an <see cref="ISchemaProvider"/> to be registered.
/// </summary>
public sealed class SchemaImportExpression : XQueryExpression
{
    /// <summary>
    /// The namespace prefix bound by this import, or <c>null</c> if no prefix was specified.
    /// When <c>default element namespace</c> is used, <see cref="IsDefaultElementNamespace"/> is true.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// True when <c>import schema default element namespace "..."</c> was used.
    /// </summary>
    public bool IsDefaultElementNamespace { get; init; }

    /// <summary>
    /// The target namespace URI of the schema to import.
    /// </summary>
    public required string TargetNamespace { get; init; }

    /// <summary>
    /// Optional location hints (URIs) for locating the schema document.
    /// </summary>
    public IReadOnlyList<string> LocationHints { get; init; } = [];

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitSchemaImport(this);
}
