using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:uri-collection() as xs:anyURI*  (default collection)
/// </summary>
public sealed class UriCollection0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "uri-collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            var docs = queryContext.DocumentResolver.ResolveCollection(null);
            var docsList = docs.ToList();
            if (docsList.Count > 0)
            {
                var uris = new List<object?>();
                foreach (var doc in docsList)
                {
                    string? docUri = null;
                    if (doc is Xdm.Nodes.XdmDocument xdmDoc)
                        docUri = xdmDoc.DocumentUri;
                    else if (doc is Xdm.Nodes.XdmNode node)
                        docUri = node.BaseUri;

                    if (docUri != null)
                        uris.Add(new Xdm.XsAnyUri(docUri));
                }
                return ValueTask.FromResult<object?>(uris.ToArray());
            }
        }

        // FODC0002: no default URI collection is available
        throw new XQueryRuntimeException("FODC0002", "No default URI collection is available");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// JSON document functions (XPath 3.1 §14.8)
// ═══════════════════════════════════════════════════════════════════════════
