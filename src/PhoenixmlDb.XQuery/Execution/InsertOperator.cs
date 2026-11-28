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
/// Insert node(s) into/before/after a target.
/// Collects an <see cref="Ast.InsertPrimitive"/> into the context's PUL.
/// </summary>
public sealed class InsertOperator : PhysicalOperator
{
    public required PhysicalOperator Source { get; init; }
    public required PhysicalOperator Target { get; init; }
    public required Ast.InsertPosition Position { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // XQUF 3.0 §5.1. The insertion sequence: an attribute may not follow other content.
        var sourceItems = new List<object?>();
        await foreach (var item in Source.ExecuteGuarded(context))
            sourceItems.Add(item);
        var content = new List<object?>(sourceItems.Count);
        foreach (var item in sourceItems)
            UpdateTargets.Flatten(item, content);
        bool attributes = false, otherContent = false;
        foreach (var item in content)
        {
            switch (UpdateTargets.KindOf(item))
            {
                case UpdateTargets.ContentKind.Attribute when otherContent:
                    throw new XQueryRuntimeException("XUTY0004",
                        "insert expression: an attribute follows content that is not an attribute in the insertion sequence.");
                case UpdateTargets.ContentKind.Attribute:
                    attributes = true;
                    break;
                case UpdateTargets.ContentKind.Other:
                    otherContent = true;
                    break;
            }
        }

        // The target: an element or a document node to insert into; an element, text, comment or
        // processing instruction with a parent to insert before or after.
        var into = Position is Ast.InsertPosition.Into or Ast.InsertPosition.AsFirstInto or Ast.InsertPosition.AsLastInto;
        var targetNode = into
            ? await UpdateTargets.SingleAsync(Target, context, "insert expression", "XUTY0005",
                "element or document node", static n => n is XdmElement or XdmDocument).ConfigureAwait(false)
            : await UpdateTargets.SingleAsync(Target, context, "insert expression", "XUTY0006",
                "element, text, comment or processing instruction",
                static n => n is XdmElement or XdmText or XdmComment or XdmProcessingInstruction).ConfigureAwait(false);
        if (!into && !UpdateTargets.HasParent(targetNode))
            throw new XQueryRuntimeException("XUDY0029", "insert expression: the target of before or after has no parent.");
        if (attributes && into && targetNode is not XdmElement)
            throw new XQueryRuntimeException("XUTY0022", "insert expression: attributes cannot be inserted into a document node.");
        if (attributes && !into && UpdateTargets.ParentOf(targetNode, context) is not XdmElement)
            throw new XQueryRuntimeException("XUDY0030",
                "insert expression: attributes cannot be inserted before or after a child of a document node.");
        if (attributes)
            UpdateTargets.RequireCompatibleBindings(content.OfType<XdmAttribute>(),
                into ? targetNode as XdmElement : UpdateTargets.ParentOf(targetNode, context) as XdmElement, "insert expression", context);

        // The content as element content (XQuery 3.1 §3.9.1.3 rule 1e, a document node as its children).
        var builder = (INodeBuilder?)context.UpdateTargetStore ?? context.NodeStore as INodeBuilder ?? targetNode.Store as INodeBuilder;
        sourceItems = context.CopyUpdateContent(UpdateTargets.AsElementContent(content, context, "insert expression", builder));
        var source = sourceItems.Count == 1 ? sourceItems[0] : sourceItems.ToArray();
        context.PendingUpdates.AddInsert(targetNode, source!, Position);

        yield break; // Update expressions produce no value, only PUL entries
    }
}
