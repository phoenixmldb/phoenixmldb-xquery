using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Rename a node.
/// Collects a <see cref="Ast.RenamePrimitive"/> into the context's PUL.
/// </summary>
public sealed class RenameOperator : PhysicalOperator
{
    public required PhysicalOperator Target { get; init; }
    public required PhysicalOperator NewName { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // XQUF 3.0 §5.4: the target is one element, attribute or processing instruction.
        var targetNode = await UpdateTargets.SingleAsync(Target, context, "rename expression", "XUTY0012",
            "element, attribute or processing instruction",
            static n => n is XdmElement or XdmAttribute or XdmProcessingInstruction).ConfigureAwait(false);

        // XQUF 3.0 §5.4: the new name is evaluated as though it were the name expression of a
        // computed element, attribute or processing instruction constructor, by the target's kind,
        // with those constructors' errors. Only the first item was taken, a string without a colon
        // became a local name unchecked, and an element's new name ignored the default element
        // namespace: a sequence, a comment, "" or "12" were renamed to, and the document then could
        // not be serialized (XQUTS id-rename-expr-020, -024, -026).
        PhoenixmlDb.Core.QName qname;
        switch (targetNode)
        {
            case XdmElement:
                qname = await ComputedElementConstructorOperator.EvaluateNameAsync(NewName, context, hasLiteralName: false).ConfigureAwait(false);
                break;
            case XdmAttribute:
                var (localName, prefix, ns) = await ComputedAttributeConstructorOperator.EvaluateNameAsync(NewName, context, hasLiteralName: false).ConfigureAwait(false);
                qname = new PhoenixmlDb.Core.QName(PhoenixmlDb.Core.NamespaceId.None, localName, prefix) { ExpandedNamespace = ns };
                break;
            default:
                // A processing instruction: fn:QName((), $NCName).
                var target = await PIConstructorOperator.EvaluateTargetAsync(null, NewName, context).ConfigureAwait(false);
                qname = new PhoenixmlDb.Core.QName(PhoenixmlDb.Core.NamespaceId.None, target);
                break;
        }

        // The new name's namespace id must be one the query's node store has interned, as for an
        // element constructor: fn:QName builds an id from the URI's hash that no store knows,
        // and the URI itself (RuntimeNamespace) was dropped here, so the renamed node lost its
        // namespace and could not be serialized (BUGS.md #129).
        var uri = qname.ResolvedNamespace;
        if (uri != null)
        {
            var nsId = uri.Length == 0
                ? PhoenixmlDb.Core.NamespaceId.None
                : context.NodeStore is INodeBuilder builder ? builder.InternNamespace(uri) : qname.Namespace;
            qname = new PhoenixmlDb.Core.QName(nsId, qname.LocalName, qname.Prefix) { RuntimeNamespace = uri };
        }

        // The binding the new name implies must not conflict with the namespaces of the element,
        // or of an attribute's parent (XQUF 3.0 §5.4, XUDY0023).
        var owner = targetNode as XdmElement ?? (targetNode is XdmAttribute ? UpdateTargets.ParentOf(targetNode, context) as XdmElement : null);
        if (owner is not null && UpdateTargets.ImpliedBinding(qname.Prefix, uri ?? "", targetNode is XdmElement) is { } binding
            && UpdateTargets.InScopeNamespaces(owner, context).TryGetValue(binding.Prefix, out var existing) && existing != binding.Uri)
            throw UpdateTargets.Conflict("XUDY0023", "rename expression", binding, existing);

        context.PendingUpdates.AddRename(targetNode, qname);

        yield break;
    }
}
