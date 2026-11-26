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
/// Replace a node with another.
/// Collects a <see cref="Ast.ReplaceNodePrimitive"/> into the context's PUL.
/// </summary>
public sealed class ReplaceNodeOperator : PhysicalOperator
{
    public required PhysicalOperator Target { get; init; }
    public required PhysicalOperator Replacement { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // XQUF 3.0 §5.3.1. The replacement sequence first, then the target.
        var replacementItems = new List<object?>();
        await foreach (var item in Replacement.ExecuteGuarded(context))
            replacementItems.Add(item);

        var targetNode = await UpdateTargets.SingleAsync(Target, context, "replace node expression", "XUTY0008",
            "element, attribute, text, comment or processing instruction",
            static n => n is XdmElement or XdmAttribute or XdmText or XdmComment or XdmProcessingInstruction).ConfigureAwait(false);
        if (!UpdateTargets.HasParent(targetNode))
            throw new XQueryRuntimeException("XUDY0009", "replace node expression: the target has no parent.");

        // An attribute is replaced by attributes; other nodes by elements, text, comments and
        // processing instructions (a document node is its children, an atomic value text).
        var content = new List<object?>(replacementItems.Count);
        foreach (var item in replacementItems)
            UpdateTargets.Flatten(item, content);
        foreach (var item in content)
        {
            var kind = UpdateTargets.KindOf(item);
            if (targetNode is XdmAttribute && kind == UpdateTargets.ContentKind.Other)
                throw new XQueryRuntimeException("XUTY0011",
                    $"replace node expression: an attribute can be replaced only by attributes, not by {UpdateTargets.Describe(item)}.");
            if (targetNode is not XdmAttribute && kind == UpdateTargets.ContentKind.Attribute)
                throw new XQueryRuntimeException("XUTY0010",
                    $"replace node expression: {UpdateTargets.Describe(targetNode)} cannot be replaced by an attribute.");
        }
        if (targetNode is XdmAttribute)
            UpdateTargets.RequireCompatibleBindings(content.OfType<XdmAttribute>(),
                UpdateTargets.ParentOf(targetNode, context) as XdmElement, "replace node expression");

        // The content as element content (XQuery 3.1 §3.9.1.3 rule 1e, a document node as its children).
        var builder = (INodeBuilder?)context.UpdateTargetStore ?? context.NodeStore as INodeBuilder ?? targetNode.Store as INodeBuilder;
        replacementItems = context.CopyUpdateContent(UpdateTargets.AsElementContent(content, context, "replace node expression", builder));
        var replacement = replacementItems.Count == 1 ? replacementItems[0] : replacementItems.ToArray();
        context.PendingUpdates.AddReplaceNode(targetNode, replacement!);

        yield break;
    }
}
