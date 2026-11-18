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

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();

        // FODC0004: invalid URI (e.g., malformed percent-escape)
        if (uri != null)
            ValidateCollectionUri(uri);

        // A local directory (dir?select=*.json;recurse=yes): its files, unless the host
        // registered a collection under that URI.
        if (uri != null && context is QueryExecutionContext dirContext && !DirectoryCollection.IsRegistered(uri, dirContext)
            && await DirectoryCollection.TryResolveAsync(uri, context, urisOnly: false).ConfigureAwait(false) is { } files)
            return files.ToArray();

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            // Check for registered non-node collections (XQuery 3.1: fn:collection returns item()*)
            if (queryContext.DocumentResolver is XdmDocumentStore store &&
                store.TryResolveCollectionItems(uri, out var items))
            {
                return items!.ToArray();
            }

            var docs = queryContext.DocumentResolver.ResolveCollection(uri).ToArray();
            if (docs.Length > 0)
                return docs;
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
