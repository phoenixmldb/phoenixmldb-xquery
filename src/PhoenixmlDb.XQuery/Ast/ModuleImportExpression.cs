using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// import module namespace prefix = "uri" at "location1", "location2";
/// </summary>
public sealed class ModuleImportExpression : XQueryExpression
{
    /// <summary>
    /// The namespace prefix bound by this import, or <c>null</c> if no prefix was specified.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// The target namespace URI of the module to import.
    /// </summary>
    public required string NamespaceUri { get; init; }

    /// <summary>
    /// Optional location hints (URIs) for locating the module source.
    /// </summary>
    public IReadOnlyList<string> LocationHints { get; init; } = [];

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitModuleImport(this);
}
