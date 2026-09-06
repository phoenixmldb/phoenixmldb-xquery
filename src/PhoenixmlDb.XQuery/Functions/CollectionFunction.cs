using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:collection($arg) as item()*
/// </summary>
public sealed class CollectionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();

        // FODC0004: invalid URI (e.g., malformed percent-escape)
        if (uri != null)
            ValidateCollectionUri(uri);

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            // Check for registered non-node collections (XQuery 3.1: fn:collection returns item()*)
            if (queryContext.DocumentResolver is XdmDocumentStore store &&
                store.TryResolveCollectionItems(uri, out var items))
            {
                return ValueTask.FromResult<object?>(items!.ToArray());
            }

            var docs = queryContext.DocumentResolver.ResolveCollection(uri).ToArray();
            if (docs.Length > 0)
                return ValueTask.FromResult<object?>(docs);
        }

        // FODC0002: collection not found
        throw new XQueryRuntimeException("FODC0002", $"Collection '{uri}' not found");
    }

    internal static void ValidateCollectionUri(string uri)
    {
        for (int i = 0; i < uri.Length; i++)
        {
            if (uri[i] == '%')
            {
                if (i + 2 >= uri.Length || !Uri.IsHexDigit(uri[i + 1]) || !Uri.IsHexDigit(uri[i + 2]))
                    throw new XQueryRuntimeException("FODC0004", $"Invalid collection URI: '{uri}'");
                i += 2;
            }
        }
    }
}
