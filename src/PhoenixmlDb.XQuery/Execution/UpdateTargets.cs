using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// The checks XQuery Update Facility 3.0 makes on the target and the content of an insert,
/// delete, replace or rename expression (§5.1-5.4) before the expression adds an update primitive.
/// </summary>
internal static class UpdateTargets
{
    /// <summary>
    /// The one node the target expression returns. Nothing is XUDY0027; more than one item, an item
    /// that is not a node, or a node <paramref name="allowed"/> refuses is the type error
    /// <paramref name="typeError"/>.
    /// </summary>
    internal static async Task<XdmNode> SingleAsync(PhysicalOperator target, QueryExecutionContext context,
        string expression, string typeError, string kinds, Func<XdmNode, bool> allowed)
    {
        var items = new List<object?>(2);
        await foreach (var item in target.ExecuteGuarded(context))
        {
            Flatten(item, items);
            if (items.Count > 1)
                break;
        }
        if (items.Count == 0)
            throw new XQueryRuntimeException("XUDY0027", $"{expression}: target is empty.");
        if (items.Count > 1)
            throw new XQueryRuntimeException(typeError, $"{expression}: the target must be a single {kinds}, not a sequence of more than one item.");
        if (items[0] is not XdmNode node || !allowed(node))
            throw new XQueryRuntimeException(typeError, $"{expression}: the target must be a single {kinds}, not {Describe(items[0])}.");
        return node;
    }

    /// <summary>The items of a sequence, a nested sequence flattened; an empty item dropped.</summary>
    internal static void Flatten(object? item, List<object?> into)
    {
        switch (item)
        {
            case null:
                return;
            case object?[] sequence:
                foreach (var member in sequence)
                    Flatten(member, into);
                return;
            default:
                into.Add(item);
                return;
        }
    }

    /// <summary>
    /// An insertion or replacement sequence as element content. XQUF 3.0 §5.1 and §5.3.1 evaluate it
    /// "as though it were an enclosed expression in an element constructor (see Rule 1e in Section
    /// 3.9.1.3 Content)", and replace a document node in it by its children. Rule 1e of XQuery 3.1
    /// §3.9.1.3: "Each array returned by the enclosed expression is flattened"; "If an enclosed
    /// expression returns a function, a type error is raised [err:XQTY0105]" (a map is a function);
    /// "For each adjacent sequence of one or more atomic values ..., a new text node is constructed,
    /// containing the result of casting each atomic value to a string, with a single space character
    /// inserted between adjacent values." Text nodes that are adjacent, and an empty one, are left to
    /// upd:applyUpdates steps 3 and 4, which <see cref="PendingUpdateApplicator.Apply"/> does in the
    /// tree: they merge an inserted text node with one already there too.
    /// </summary>
    /// <remarks>
    /// Only nodes are placed when the list is applied, and the content was kept as it was: an atomic
    /// value, a map, a function and an array were lost with no error (<c>insert node 'x' before
    /// $d/r</c>, <c>replace node $t with 'x'</c> changed nothing), and in a copy modify expression a
    /// document node was placed as a child.
    /// </remarks>
    /// <param name="content">The insertion or replacement sequence, flattened.</param>
    /// <param name="context">The context of the update expression.</param>
    /// <param name="expression">The expression, for the error messages.</param>
    /// <param name="builder">The store the text nodes are made in: the copy modify overlay, the query's
    /// store, or the target's store (a host may give the query no store that builds nodes). A text node
    /// with no store would have the id 0, and two of them one id.</param>
    internal static List<object?> AsElementContent(List<object?> content, QueryExecutionContext context, string expression, INodeBuilder? builder)
    {
        var items = new List<object?>(content.Count);
        foreach (var item in content)
            Expand(item);

        var result = new List<object?>(items.Count);
        System.Text.StringBuilder? text = null;
        var previousAtomic = false;
        foreach (var item in items)
        {
            switch (item)
            {
                case XdmNode:
                    Flush();
                    result.Add(item);
                    previousAtomic = false;
                    break;
                default:
                    if (previousAtomic)
                        Append(" ");
                    Append(Functions.ConcatFunction.XQueryStringValue(item));
                    previousAtomic = true;
                    break;
            }
        }
        Flush();
        return result;

        void Expand(object? item)
        {
            switch (item)
            {
                case null:
                    return;
                case object?[] sequence:
                    foreach (var member in sequence)
                        Expand(member);
                    return;
                case List<object?> array:
                    foreach (var member in array)
                        Expand(member);
                    return;
                case XdmDocument document:
                    foreach (var child in document.Children)
                    {
                        if (context.LoadNode(document, child) is { } node)
                            items.Add(node);
                    }
                    return;
                default:
                    // The check of element and document constructors: one rule 1e.
                    ElementConstructorOperator.RejectFunctionItemContent(item);
                    items.Add(item);
                    return;
            }
        }

        void Append(string value)
            => StringLengthLimit.Append(context, text ??= new System.Text.StringBuilder(), value, "An insertion sequence");

        void Flush()
        {
            if (text is null)
                return;
            var store = builder ?? throw new InvalidOperationException(
                $"{expression}: no node store can make the text node of its atomic values (the query has no store that builds nodes, and the target's store does not).");
            var node = new XdmText { Id = store.AllocateId(), Document = new DocumentId(0), Value = text.ToString() };
            store.RegisterNode(node);
            result.Add(node);
            text = null;
        }
    }

    internal static bool HasParent(XdmNode node) => node.Parent is { } parent && parent != NodeId.None;

    /// <summary>The parent of <paramref name="node"/>, or <c>null</c>.</summary>
    internal static XdmNode? ParentOf(XdmNode node, QueryExecutionContext context)
        => HasParent(node) ? context.LoadNode(node, node.Parent) : null;

    // What content processing makes of an item of an insertion or replacement sequence (XQuery 3.1
    // §3.9.1.3, which §5.1 and §5.3.1 follow): an attribute stays an attribute; a document node is
    // its children, never attributes, or nothing when it has none; any other node, and an atomic
    // value (a text node), is content that is not an attribute.
    internal enum ContentKind { None, Attribute, Other }

    internal static ContentKind KindOf(object? item) => item switch
    {
        XdmAttribute => ContentKind.Attribute,
        XdmDocument { Children.Count: 0 } => ContentKind.None,
        null => ContentKind.None,
        _ => ContentKind.Other,
    };

    /// <summary>
    /// The in-scope namespaces of an element: prefix (empty for the default) to URI, read as
    /// fn:namespace-uri-for-prefix reads them (<see cref="NodeNamespaces.UriOf"/>). The URI was asked
    /// of the element's store alone, which does not know an id a copy kept from its source: the
    /// default namespace of a copied element read as none, and a rename into that namespace was
    /// refused with XUDY0023.
    /// </summary>
    internal static Dictionary<string, string> InScopeNamespaces(XdmElement element, QueryExecutionContext? context = null)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (prefix, ns) in AxisNavigationOperator.GatherInScopeNamespaces(element,
            id => context?.LoadNode(element, id) as XdmNode ?? element.Store?.GetNode(id)))
            bindings.TryAdd(prefix, NodeNamespaces.UriOf(element, ns, context) ?? "");
        return bindings;
    }

    /// <summary>
    /// The namespace binding a name implies (XQUF 3.0 §5.1, §5.3.1, §5.4): its prefix and URI; for an
    /// element name with no prefix, the default namespace when it has a URI; none for a name in no
    /// namespace, an unprefixed attribute, or the xml prefix.
    /// </summary>
    internal static (string Prefix, string Uri)? ImpliedBinding(string? prefix, string uri, bool element)
    {
        prefix ??= "";
        if (prefix == "xml")
            return null;
        if (prefix.Length > 0)
            return (prefix, uri);
        return element && uri.Length > 0 ? ("", uri) : null;
    }

    /// <summary>The binding an attribute's name implies.</summary>
    internal static (string Prefix, string Uri)? ImpliedBinding(XdmAttribute attribute)
        => ImpliedBinding(attribute.Prefix,
            attribute.Namespace == NamespaceId.None ? "" : attribute.Store?.GetNamespaceUri(attribute.Namespace) ?? "", element: false);

    /// <summary>
    /// The attributes an insert or replace expression adds to <paramref name="owner"/>: a binding
    /// their names imply that conflicts with one in the owner's namespaces is XUDY0023, and two of
    /// them that conflict with each other XUDY0024 (XQUF 3.0 §5.1, §5.3.1).
    /// </summary>
    internal static void RequireCompatibleBindings(IEnumerable<XdmAttribute> attributes, XdmElement? owner, string expression,
        QueryExecutionContext? context = null)
    {
        Dictionary<string, string>? inScope = null;
        var added = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            if (ImpliedBinding(attribute) is not { } binding)
                continue;
            if (owner is not null && (inScope ??= InScopeNamespaces(owner, context)).TryGetValue(binding.Prefix, out var existing)
                && existing != binding.Uri)
                throw Conflict("XUDY0023", expression, binding, existing);
            if (added.TryGetValue(binding.Prefix, out var other) && other != binding.Uri)
                throw Conflict("XUDY0024", expression, binding, other);
            added[binding.Prefix] = binding.Uri;
        }
    }

    internal static XQueryRuntimeException Conflict(string code, string expression, (string Prefix, string Uri) binding, string other)
        => new(code, $"{expression}: the prefix '{binding.Prefix}' would be bound to {binding.Uri} and to {other} on one element.");

    internal static string Describe(object? item) => item switch
    {
        XdmElement => "an element",
        XdmAttribute => "an attribute",
        XdmDocument => "a document node",
        XdmText => "a text node",
        XdmComment => "a comment",
        XdmProcessingInstruction => "a processing instruction",
        XdmNode => "a node of another kind",
        IDictionary<object, object?> => "a map",
        List<object?> => "an array",
        Ast.XQueryFunction => "a function",
        _ => "an atomic value",
    };
}
