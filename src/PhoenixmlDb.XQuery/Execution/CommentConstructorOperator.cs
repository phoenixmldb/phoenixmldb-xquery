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
/// Comment constructor operator — creates an XdmComment node.
/// Used for comment { "content" } and &lt;!-- content --&gt; expressions.
/// </summary>
public sealed class CommentConstructorOperator : PhysicalOperator
{
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeStore as INodeBuilder;

        var sb = new StringBuilder();
        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                var atomized = context.AtomizeWithNodes(item);
                sb.Append(Functions.ConcatFunction.XQueryStringValue(atomized));
            }
        }

        var value = sb.ToString();
        // XQDY0072: comment content must not contain '--' or end with '-'
        if (value.Contains("--"))
            throw new XQueryRuntimeException("XQDY0072",
                "Computed comment must not contain '--'");
        if (value.EndsWith('-'))
            throw new XQueryRuntimeException("XQDY0072",
                "Computed comment must not end with '-'");

        var comment = new XdmComment
        {
            Id = store?.AllocateId() ?? new NodeId(0),
            Document = new DocumentId(0),
            Value = value
        };
        store?.RegisterNode(comment);
        yield return comment;
    }
}
