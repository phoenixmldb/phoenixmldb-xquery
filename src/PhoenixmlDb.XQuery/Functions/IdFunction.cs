using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:id($arg as xs:string*) as element()* — returns elements with matching ID attributes
/// using the context item's document tree.
/// </summary>
public sealed class IdFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "id");
    public override XdmSequenceType ReturnType => new()
    {
        ItemType = ItemType.Element,
        Occurrence = Occurrence.ZeroOrMore
    };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType
            { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var store = context.NodeStore;
        var contextItem = context.ContextItem;
        XdmDocument? doc = null;
        if (contextItem is XdmDocument d)
            doc = d;
        else if (contextItem is XdmNode n && store != null)
            doc = FindDocumentForNode(n, store);
        if (doc == null)
            throw context.Error("FODC0001", "FODC0001: No context document for fn:id");

        return ValueTask.FromResult<object?>(FindElementsById(arguments[0], doc, store!));
    }

    /// <summary>
    /// Finds elements whose ID attributes match the given IDREFS values, or whose
    /// own content is xs:ID-typed with matching value (fn:id semantics).
    /// </summary>
    internal static object?[] FindElementsById(object? arg, XdmDocument doc, INodeStore store)
    {
        var idValues = new HashSet<string>(StringComparer.Ordinal);
        CollectIdValues(arg, idValues, store);
        if (idValues.Count == 0)
            return Array.Empty<object?>();

        var results = new List<object?>();
        WalkForIds(doc, idValues, results, store, returnParent: false);
        return results.ToArray();
    }

    /// <summary>
    /// Finds elements whose ID attributes match, or whose CHILD element has xs:ID-typed
    /// content with matching value (fn:element-with-id semantics). When the match is on a
    /// child element's xs:ID content, returns the PARENT element (the one containing the ID child).
    /// </summary>
    internal static object?[] FindElementsWithId(object? arg, XdmDocument doc, INodeStore store)
    {
        var idValues = new HashSet<string>(StringComparer.Ordinal);
        CollectIdValues(arg, idValues, store);
        if (idValues.Count == 0)
            return Array.Empty<object?>();

        var results = new List<object?>();
        WalkForIds(doc, idValues, results, store, returnParent: true);
        return results.ToArray();
    }

    /// <summary>
    /// Whitespace characters used to tokenize IDREFS values per XML spec (space, tab, CR, LF).
    /// </summary>
    private static readonly char[] WhitespaceChars = [' ', '\t', '\r', '\n'];

    internal static void CollectIdValues(object? arg, HashSet<string> ids, INodeProvider? nodeProvider = null)
    {
        if (arg == null)
            return;
        if (arg is string s)
        {
            foreach (var part in s.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries))
                ids.Add(part);
        }
        else if (arg is object?[] arr)
        {
            foreach (var item in arr)
                CollectIdValues(item, ids, nodeProvider);
        }
        else if (arg is IEnumerable<object?> seq)
        {
            foreach (var item in seq)
                CollectIdValues(item, ids, nodeProvider);
        }
        else if (arg is XdmNode node)
        {
            // Storage-deserialized element/document nodes carry a NULL precomputed string value;
            // walk the provider to compute their string value, mirroring fn:string() (#163).
            var str = node switch
            {
                XdmElement elem => Execution.QueryExecutionContext.ComputeElementStringValue(elem, nodeProvider),
                XdmDocument doc => Execution.QueryExecutionContext.ComputeDocumentStringValue(doc, nodeProvider),
                _ => node.StringValue,
            };
            foreach (var part in str.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries))
                ids.Add(part);
        }
        else
        {
            ids.Add(arg.ToString()!);
        }
    }

    private static void WalkForIds(XdmNode node, HashSet<string> ids, List<object?> results,
        INodeStore store, bool returnParent)
    {
        if (node is XdmElement elem)
        {
            bool added = false;

            // Match on xs:ID-typed attribute
            foreach (var attr in store.GetAttributes(elem))
            {
                if (attr.IsId && ids.Contains(attr.Value))
                {
                    results.Add(elem);
                    added = true;
                    break;
                }
            }

            // Match on element's own xs:ID-typed content (fn:id semantics).
            // For fn:element-with-id (returnParent=true), the element matches via its CHILD's
            // xs:ID content instead — handled in the child-walk below.
            if (!added && !returnParent && elem.IsIdContent)
            {
                // The typed value of an xs:ID element is the tokenized string content.
                // A singleton xs:ID value with internal whitespace wouldn't be a valid NCName,
                // so we check the trimmed full string. Compute via the store (an INodeProvider)
                // so storage-deserialized elements (NULL StringValue) resolve correctly (#163).
                var v = Execution.QueryExecutionContext.ComputeElementStringValue(elem, store).Trim();
                if (ids.Contains(v))
                {
                    results.Add(elem);
                    added = true;
                }
            }

            // For fn:element-with-id, check direct children for xs:ID-typed content.
            if (!added && returnParent)
            {
                foreach (var childId in elem.Children)
                {
                    if (store.GetNode(childId) is XdmElement childElem &&
                        childElem.IsIdContent)
                    {
                        var v = Execution.QueryExecutionContext.ComputeElementStringValue(childElem, store).Trim();
                        if (ids.Contains(v))
                        {
                            results.Add(elem);
                            added = true;
                            break;
                        }
                    }
                }
            }

            // Recurse into children
            foreach (var childId in elem.Children)
            {
                var child = store.GetNode(childId);
                if (child != null)
                    WalkForIds(child, ids, results, store, returnParent);
            }
        }
        else if (node is XdmDocument doc)
        {
            foreach (var childId in doc.Children)
            {
                var child = store.GetNode(childId);
                if (child != null)
                    WalkForIds(child, ids, results, store, returnParent);
            }
        }
    }

    /// <summary>
    /// Walks up the parent chain to find the document node for a given node.
    /// </summary>
    internal static XdmDocument? FindDocumentForNode(XdmNode node, INodeStore store)
    {
        var current = node;
        while (current != null)
        {
            if (current is XdmDocument doc)
                return doc;
            if (current.Parent.HasValue && current.Parent.Value != NodeId.None)
                current = store.GetNode(current.Parent.Value);
            else
                return null;
        }
        return null;
    }
}
