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

    /// <summary>The in-scope namespaces of an element: prefix (empty for the default) to URI.</summary>
    internal static Dictionary<string, string> InScopeNamespaces(XdmElement element)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (prefix, ns) in AxisNavigationOperator.GatherInScopeNamespaces(element, id => element.Store?.GetNode(id)))
            bindings.TryAdd(prefix, element.Store?.GetNamespaceUri(ns) ?? "");
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
    internal static void RequireCompatibleBindings(IEnumerable<XdmAttribute> attributes, XdmElement? owner, string expression)
    {
        Dictionary<string, string>? inScope = null;
        var added = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            if (ImpliedBinding(attribute) is not { } binding)
                continue;
            if (owner is not null && (inScope ??= InScopeNamespaces(owner)).TryGetValue(binding.Prefix, out var existing)
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
        _ => "an atomic value",
    };
}
