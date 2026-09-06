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
/// Text constructor operator — creates an XdmText node.
/// Used for text { "content" } expressions.
/// </summary>
public sealed class TextConstructorOperator : PhysicalOperator
{
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeStore as INodeBuilder;

        var sb = new StringBuilder();
        bool hasItems = false;
        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (hasItems)
                    sb.Append(' ');
                hasItems = true;
                var atomized = context.AtomizeWithNodes(item);
                sb.Append(Functions.ConcatFunction.XQueryStringValue(atomized));
            }
        }

        // Per XQuery 3.1 §3.7.3.4: if the content expression evaluates to the empty sequence,
        // no text node is constructed. But a zero-length string DOES produce a text node.
        if (!hasItems)
            yield break;

        var textNode = new XdmText
        {
            Id = store?.AllocateId() ?? new NodeId(0),
            Document = new DocumentId(0),
            Value = sb.ToString()
        };
        store?.RegisterNode(textNode);
        yield return textNode;
    }
}
