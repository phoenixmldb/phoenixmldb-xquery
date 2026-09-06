using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Placeholder for execution context (defined elsewhere).
/// </summary>
public interface ExecutionContext
{
    /// <summary>
    /// Gets the current context item.
    /// </summary>
    object? ContextItem { get; }

    /// <summary>
    /// Gets the static base URI for this execution context.
    /// Used by fn:static-base-uri() and fn:resolve-uri($relative).
    /// </summary>
    string? StaticBaseUri => null;

    /// <summary>
    /// Gets the node store for tree navigation and construction.
    /// Used by fn:path, fn:id, fn:xml-to-json, fn:parse-xml, etc.
    /// </summary>
    INodeStore? NodeStore => null;

    /// <summary>
    /// Gets the decimal format properties for format-number().
    /// Key is the format name (empty string for the default decimal format).
    /// </summary>
    IReadOnlyDictionary<string, Analysis.DecimalFormatProperties>? DecimalFormats => null;

    /// <summary>
    /// Mapping from resolved absolute URI to local file path.
    /// Used by fn:unparsed-text to resolve http:// URIs to local resource files.
    /// </summary>
    IReadOnlyDictionary<string, string>? ResourceMappings => null;
}
