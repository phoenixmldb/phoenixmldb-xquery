using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:namespace-uri-for-prefix($prefix, $element) as xs:anyURI?
/// </summary>
public sealed class NamespaceUriForPrefixFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "namespace-uri-for-prefix");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "prefix"), Type = XdmSequenceType.OptionalString },
            new() { Name = new QName(NamespaceId.None, "element"), Type = XdmSequenceType.OptionalNode }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var prefix = arguments[0]?.ToString() ?? "";
        var element = arguments[1] as XdmElement;
        if (element == null) return ValueTask.FromResult<object?>(null);

        // The 'xml' prefix is always implicitly bound
        if (prefix == "xml")
            return ValueTask.FromResult<object?>(new Xdm.XsAnyUri("http://www.w3.org/XML/1998/namespace"));

        var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;

        // Resolve against the element's IN-SCOPE namespaces (F&O §14), not just the bindings it
        // declares itself. Use the same shared routine fn:in-scope-prefixes and the namespace::
        // axis use, so the three can never disagree: it materialises constructed elements'
        // complete own set and walks ancestors for parsed ones, honouring xmlns=""
        // undeclarations and the no-inherit marker.
        //
        // Previously this read only element.NamespaceDeclarations plus the element's own prefix,
        // so a prefix inherited from an ancestor resolved to the empty sequence while
        // in-scope-prefixes happily listed it. The pairing of the two is the idiomatic way to
        // copy namespaces —
        //     for-each(in-scope-prefixes($e)) { xsl:namespace name="{.}"
        //                                       select="namespace-uri-for-prefix(., $e)" }
        // — and it raised XTDE0930 ("zero-length string, but a prefix was specified") on the
        // first inherited binding. That is XSpec's x:copy-of-namespaces, and it accounted for
        // 103 of the 162 XSLT suites in the census.
        // The DEFAULT namespace is settled by the element itself, before any ancestor walk: an
        // unprefixed element is in the default namespace by definition, so an element in no
        // namespace proves there is no in-scope default — whether because none was ever declared
        // or because an ancestor's was undeclared with xmlns="". The shared gather reports such
        // an undeclaration as an in-scope empty prefix still carrying the ancestor's URI, so
        // walking it would wrongly resurrect that URI here. (The pre-walk implementation got
        // this right by accident, having never looked past the element.)
        if (string.IsNullOrEmpty(prefix) && element.Namespace == NamespaceId.None)
            return ValueTask.FromResult<object?>(null);

        var nodeStore = context.NodeStore;
        foreach (var (nsPrefix, nsId) in Execution.AxisNavigationOperator.GatherInScopeNamespaces(
            element, id => nodeStore?.GetNode(id) as XdmNode))
        {
            if (nsPrefix != prefix)
                continue;
            // Per XQuery 3.0+: an absent default namespace is the empty sequence, not a
            // zero-length URI. Test the RESOLVED uri, not just NamespaceId.None: an xmlns=""
            // undeclaration reaches us from the shared gather as a prefix that is in scope
            // bound to the empty URI, so checking the id alone let it through as a one-item
            // sequence containing "".
            var uri = NamespaceUriFunction.ResolveNsId(nsId, qec);
            if (string.IsNullOrEmpty(uri))
                return ValueTask.FromResult<object?>(null);
            return ValueTask.FromResult<object?>(uri);
        }
        return ValueTask.FromResult<object?>(null);
    }
}
