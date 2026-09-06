using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:uri-collection($arg as xs:string?) as xs:anyURI*
/// Returns the URIs of documents in a collection.
/// </summary>
public sealed class UriCollectionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "uri-collection");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = arguments[0]?.ToString();

        // FODC0004: invalid URI
        if (uri != null)
            CollectionFunction.ValidateCollectionUri(uri);

        if (context is QueryExecutionContext queryContext && queryContext.DocumentResolver is not null)
        {
            var docs = queryContext.DocumentResolver.ResolveCollection(uri);
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

        // FODC0002: collection not found
        throw new XQueryRuntimeException("FODC0002", $"URI collection '{uri}' not found");
    }
}
