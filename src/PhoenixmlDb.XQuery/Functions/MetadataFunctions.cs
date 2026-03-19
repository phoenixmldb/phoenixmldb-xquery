using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// dbxml:metadata($node as node(), $key as xs:string) as item()?
/// Retrieves a single metadata value for the document containing the given node.
/// </summary>
public sealed class MetadataGetFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Dbxml, "metadata", "dbxml");

    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;

    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.Node },
        new() { Name = new QName(NamespaceId.None, "key"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var node = arguments[0] as XdmNode;
        if (node is null)
            return ValueTask.FromResult<object?>(null);

        var key = arguments[1]?.ToString() ?? string.Empty;
        var documentId = node.Document;

        // Handle system metadata keys (prefixed with "dbxml:")
        if (key.StartsWith("dbxml:", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(ResolveSystemMetadata(documentId, key, context));
        }

        // User metadata — delegate to the metadata provider
        if (context is QueryExecutionContext queryContext && queryContext.MetadataProvider is not null)
        {
            var rawValue = queryContext.MetadataProvider.GetMetadata(documentId, key);
            if (rawValue is null)
                return ValueTask.FromResult<object?>(null);

            return ValueTask.FromResult<object?>(Encoding.UTF8.GetString(rawValue));
        }

        return ValueTask.FromResult<object?>(null);
    }

    private static object? ResolveSystemMetadata(DocumentId documentId, string key, Ast.ExecutionContext context)
    {
        // System metadata is resolved through the metadata resolver with the full key.
        // The host application is responsible for mapping system keys to actual values.
        if (context is QueryExecutionContext queryContext && queryContext.MetadataProvider is not null)
        {
            var rawValue = queryContext.MetadataProvider.GetMetadata(documentId, key);
            if (rawValue is null)
                return null;

            return key switch
            {
                "dbxml:size" or "dbxml:node-count" =>
                    long.TryParse(Encoding.UTF8.GetString(rawValue), out var num) ? num : Encoding.UTF8.GetString(rawValue),
                "dbxml:created" or "dbxml:modified" =>
                    Encoding.UTF8.GetString(rawValue),
                _ => Encoding.UTF8.GetString(rawValue)
            };
        }

        return null;
    }
}

/// <summary>
/// dbxml:metadata($node as node()) as map(xs:string, item()?)
/// Retrieves all user metadata for the document containing the given node as a map.
/// </summary>
public sealed class MetadataAllFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Dbxml, "metadata", "dbxml");

    public override XdmSequenceType ReturnType => new()
    {
        ItemType = ItemType.Map,
        Occurrence = Occurrence.ExactlyOne
    };

    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.Node }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var node = arguments[0] as XdmNode;
        if (node is null)
            return ValueTask.FromResult<object?>(new Dictionary<object, object?>());

        var documentId = node.Document;

        if (context is QueryExecutionContext queryContext && queryContext.MetadataProvider is not null)
        {
            var entries = queryContext.MetadataProvider.GetAllMetadata(documentId);
            var map = new Dictionary<object, object?>();

            foreach (var (key, value) in entries)
            {
                map[key] = Encoding.UTF8.GetString(value);
            }

            return ValueTask.FromResult<object?>(map);
        }

        return ValueTask.FromResult<object?>(new Dictionary<object, object?>());
    }
}
