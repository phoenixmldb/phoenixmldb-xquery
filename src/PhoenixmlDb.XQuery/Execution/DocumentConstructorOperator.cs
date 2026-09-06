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
/// Document constructor operator — creates an XdmDocument node wrapping content.
/// Used for document { content } expressions.
/// </summary>
public sealed class DocumentConstructorOperator : PhysicalOperator
{
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeStore as INodeBuilder;

        if (store == null)
        {
            // Without a store, delegate to content directly
            await foreach (var item in ContentOperator.ExecuteAsync(context))
            {
                yield return item;
            }
            yield break;
        }

        var constructedDocId = new DocumentId(0);
        var docId = store.AllocateId();
        var childIds = new List<NodeId>();
        StringBuilder? pendingText = null;

        void FlushPendingText()
        {
            if (pendingText != null && pendingText.Length > 0)
            {
                var textId = store.AllocateId();
                var textNode = new XdmText
                {
                    Id = textId,
                    Document = constructedDocId,
                    Value = pendingText.ToString()
                };
                textNode.Parent = docId;
                store.RegisterNode(textNode);
                childIds.Add(textId);
                pendingText.Clear();
            }
        }

        NodeId? docElement = null;
        bool lastWasAtomic = false;

        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item is XdmElement childElem)
            {
                FlushPendingText();
                var copyId = ElementConstructorOperator.DeepCopyNode(childElem, store, constructedDocId, docId);
                childIds.Add(copyId);
                docElement ??= copyId;
                lastWasAtomic = false;
            }
            else if (item is XdmComment || item is XdmProcessingInstruction)
            {
                FlushPendingText();
                var copyId = ElementConstructorOperator.DeepCopyNode((XdmNode)item, store, constructedDocId, docId);
                childIds.Add(copyId);
                lastWasAtomic = false;
            }
            else if (item is XdmAttribute badAttr)
            {
                // XPTY0004: document content sequence may not contain attribute nodes.
                // D7: surface the offending attribute's source position as a related
                // location so LSP adapters can jump from the constructor site to the
                // input data that violated the constraint.
                throw new XQueryRuntimeException("XPTY0004",
                    "A document constructor cannot contain attribute nodes")
                {
                    RelatedLocations = badAttr.SourceLine > 0
                        ? [new Ast.SourceLocation(badAttr.SourceLine, badAttr.SourceColumn, 0, -1)]
                        : Array.Empty<Ast.SourceLocation>()
                };
            }
            else if (item is XdmText text)
            {
                pendingText ??= new StringBuilder();
                pendingText.Append(text.Value);
                lastWasAtomic = false;
            }
            else if (item is XdmDocument nestedDoc)
            {
                // Unwrap nested document: per XQuery spec, document nodes in content
                // are replaced by their children. Text children merge with pending text.
                foreach (var nestedChildId in nestedDoc.Children)
                {
                    var nestedChild = store.GetNode(nestedChildId);
                    if (nestedChild is XdmText nestedText)
                    {
                        // Merge text from nested doc into pending text
                        pendingText ??= new StringBuilder();
                        pendingText.Append(nestedText.Value);
                    }
                    else if (nestedChild != null)
                    {
                        FlushPendingText();
                        var copyId = ElementConstructorOperator.DeepCopyNode(nestedChild, store, constructedDocId, docId);
                        childIds.Add(copyId);
                        if (nestedChild is XdmElement && docElement == null)
                            docElement = copyId;
                    }
                }
                lastWasAtomic = false;
            }
            else if (item != null)
            {
                pendingText ??= new StringBuilder();
                var atomicVal = context.AtomizeWithNodes(item)?.ToString() ?? "";
                // Space-separate consecutive atomic values per XQuery 3.1 §3.7.3.4
                // Empty strings still count as atomic values requiring a separator
                if (lastWasAtomic)
                    pendingText.Append(' ');
                pendingText.Append(atomicVal);
                lastWasAtomic = true;
            }
        }

        FlushPendingText();

        var doc = new XdmDocument
        {
            Id = docId,
            Document = constructedDocId,
            Children = childIds,
            DocumentElement = docElement,
            BaseUri = context.StaticBaseUri
        };
        doc.Parent = null;

        // Pre-compute string value by concatenating all descendant text nodes
        doc._stringValue = QueryExecutionContext.ComputeDocumentStringValue(doc, store);

        store.RegisterNode(doc);

        yield return doc;
    }
}
