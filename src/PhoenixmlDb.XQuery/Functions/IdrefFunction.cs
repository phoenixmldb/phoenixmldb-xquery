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
        // An atomic value where the node belongs is a type error, not "no document":
        // (1, 2, 3)[idref("x", .)] is XPTY0004 (QT3 K2-SeqIDREFFunc-2, fn-idref-3).
        if (nodeArg is not XdmNode)
            throw new XQueryRuntimeException("XPTY0004",
                "fn:idref: the second argument must be a node");
        if (doc == null)
            throw new XQueryRuntimeException("FODC0001",
                "fn:idref: node is not in a tree rooted at a document node");

        return ValueTask.FromResult<object?>(FindNodesByIdref(arguments[0], doc, store!,
            (context as QueryExecutionContext)?.SchemaProvider));
    }

    /// <summary>
    /// Finds attribute (or element) nodes whose IDREF-typed values match the given ID values.
    /// Returns nodes in document order, with duplicates removed.
    /// </summary>
    internal static object?[] FindNodesByIdref(object? arg, XdmDocument doc, INodeStore store,
        ISchemaProvider? schemas = null)
    {
        var idValues = new HashSet<string>(StringComparer.Ordinal);
        IdFunction.CollectIdValues(arg, idValues);
        if (idValues.Count == 0)
            return Array.Empty<object?>();

        var results = new List<object?>();
        WalkForIdrefs(doc, idValues, results, store, schemas);
        return results.ToArray();
    }

    /// <summary>
    /// Whether a node has the is-idrefs property (XDM 3.1 §5.6, §6.2.2): its typed value
    /// contains at least one xs:IDREF. That is a DTD-declared IDREF or IDREFS attribute, or an
    /// element or attribute a schema types as xs:IDREF, xs:IDREFS, a type derived from one, or a
    /// list or union one of whose values was validated as an IDREF. Only DTD attributes were
    /// recognised, so fn:idref found nothing in a schema-validated document.
    /// </summary>
    private static bool IsIdrefs(Xdm.XdmTypeName annotation, string value, ISchemaProvider? schemas)
    {
        if (annotation.Namespace == NamespaceId.Xsd)
            return annotation.LocalName is "IDREF" or "IDREFS";
        return schemas is not null && schemas.HasIdrefTypedValue(annotation, value);
    }

    /// <summary>Every token of an is-idrefs node is a candidate IDREF (F&amp;O 3.1 §14.5.5).</summary>
    private static bool AnyTokenIn(string value, HashSet<string> ids)
    {
        foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (ids.Contains(token))
                return true;
        return false;
    }

    private static void WalkForIdrefs(XdmNode node, HashSet<string> ids, List<object?> results,
        INodeStore store, ISchemaProvider? schemas)
    {
        if (node is XdmElement elem)
        {
            if (elem.TypeAnnotation != Xdm.XdmTypeName.Untyped && elem.TypeAnnotation != default)
            {
                var content = QueryExecutionContext.ComputeElementStringValue(elem, store);
                if (IsIdrefs(elem.TypeAnnotation, content, schemas) && AnyTokenIn(content, ids))
                    results.Add(elem);
            }
            foreach (var attr in store.GetAttributes(elem))
            {
                if ((attr.IsIdRef || IsIdrefs(attr.TypeAnnotation, attr.Value, schemas)) && AnyTokenIn(attr.Value, ids))
                    results.Add(attr);
            }
            foreach (var childId in elem.Children)
            {
                var child = store.GetNode(childId);
                if (child != null)
                    WalkForIdrefs(child, ids, results, store, schemas);
            }
        }
        else if (node is XdmDocument doc)
        {
            foreach (var childId in doc.Children)
            {
                var child = store.GetNode(childId);
                if (child != null)
                    WalkForIdrefs(child, ids, results, store, schemas);
            }
        }
    }
}
