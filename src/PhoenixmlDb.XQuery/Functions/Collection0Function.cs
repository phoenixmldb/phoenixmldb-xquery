using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:collection() as item()*  (default collection)
/// </summary>
public sealed class Collection0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            // Check for registered non-node collections (XQuery 3.1: fn:collection returns item()*)
            if (queryContext.DocumentResolver is XdmDocumentStore store &&
                store.TryResolveCollectionItems(null, out var items))
            {
                return ValueTask.FromResult<object?>(items!.ToArray());
            }

            var docs = queryContext.DocumentResolver.ResolveCollection(null).ToArray();
            if (docs.Length > 0)
                return ValueTask.FromResult<object?>(docs);
        }

        // FODC0002: no default collection is available
        throw new XQueryRuntimeException("FODC0002", "No default collection is available");
    }
}
