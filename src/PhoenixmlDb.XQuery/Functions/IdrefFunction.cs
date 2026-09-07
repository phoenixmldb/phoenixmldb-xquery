using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:idref($arg as xs:string*, $node as node()) as node()*</summary>
public sealed class IdrefFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "idref");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "node"), Type = new() { ItemType = ItemType.Node, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var store = context.NodeStore;
        var nodeArg = arguments[1];
        XdmDocument? doc = null;
        if (nodeArg is XdmDocument d)
            doc = d;
        else if (nodeArg is XdmNode n && store != null)
            doc = IdFunction.FindDocumentForNode(n, store);
        if (doc == null)
            throw new XQueryRuntimeException("FODC0001",
                "fn:idref: node is not in a tree rooted at a document node");

        return ValueTask.FromResult<object?>(FindNodesByIdref(arguments[0], doc, store!));
    }

    /// <summary>
    /// Finds attribute (or element) nodes whose IDREF-typed values match the given ID values.
    /// Returns nodes in document order, with duplicates removed.
    /// </summary>
    internal static object?[] FindNodesByIdref(object? arg, XdmDocument doc, INodeStore store)
    {
        var idValues = new HashSet<string>(StringComparer.Ordinal);
        IdFunction.CollectIdValues(arg, idValues);
        if (idValues.Count == 0)
            return Array.Empty<object?>();

        var results = new List<object?>();
        WalkForIdrefs(doc, idValues, results, store);
        return results.ToArray();
    }

    private static void WalkForIdrefs(XdmNode node, HashSet<string> ids, List<object?> results,
        INodeStore store)
    {
        if (node is XdmElement elem)
        {
            foreach (var attr in store.GetAttributes(elem))
            {
                if (attr.IsIdRef)
                {
                    // IDREF values are space-separated; each token is checked against the ID set
                    foreach (var token in attr.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (ids.Contains(token))
                        {
                            results.Add(attr);
                            break; // Only add this attribute once
                        }
                    }
                }
            }
            foreach (var childId in elem.Children)
            {
                var child = store.GetNode(childId);
                if (child != null)
                    WalkForIdrefs(child, ids, results, store);
            }
        }
        else if (node is XdmDocument doc)
        {
            foreach (var childId in doc.Children)
            {
                var child = store.GetNode(childId);
                if (child != null)
                    WalkForIdrefs(child, ids, results, store);
            }
        }
    }
}
