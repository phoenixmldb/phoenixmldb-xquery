using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Namespace declaration in element constructor.
/// </summary>
public sealed class NamespaceDeclaration
{
    public required string Prefix { get; init; }
    public required string Uri { get; init; }
}
