using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:innermost($nodes as node()*) as node()*</summary>
public sealed class InnermostFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "innermost");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "nodes"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        // Return nodes that have no descendants in the set, in document order
        var nodes = arguments[0] is IList<object?> list ? list : arguments[0] is IEnumerable<object?> seq ? seq.ToList() : [arguments[0]];

        // Type check: all items must be nodes
        foreach (var item in nodes)
        {
            if (item != null && item is not XdmNode)
                throw context.Error("XPTY0004", $"Argument to fn:innermost contains a non-node item of type {item.GetType().Name}");
        }

        if (nodes.Count <= 1) return ValueTask.FromResult(arguments[0]);

        var qec = context as Execution.QueryExecutionContext;
        // Deduplicate by document-order key, preserving first occurrence. Keying on
        // DocumentOrderKey (not bare NodeId) keeps distinct cross-store nodes that share a
        // NodeId from collapsing into one (issue #188).
        var seen = new HashSet<(ulong, NodeId)>();
        var nodeList = new List<XdmNode>();
        foreach (var n in nodes.OfType<XdmNode>())
        {
            if (seen.Add(n.DocumentOrderKey))
                nodeList.Add(n);
        }
        // Build a set of NodeIds in the input for fast ancestor-membership lookup. This is a
        // structural (within-tree) test, not a document-order comparison: a node's ancestors
        // always live in the same tree/store, so NodeId is the correct key. Cross-store parent
        // resolution (escaping nodes) is out of scope for #188.
        var nodeIdSet = new HashSet<NodeId>(nodeList.Select(n => n.Id));

        var result = new List<XdmNode>();
        foreach (var node in nodeList)
        {
            bool hasDescendantInSet = false;
            // Check if any other node in the set has this node as an ancestor
            foreach (var other in nodeList)
            {
                if (other.Id == node.Id) continue;
                var parentId = other.Parent;
                while (parentId.HasValue && parentId.Value != NodeId.None)
                {
                    if (parentId.Value == node.Id)
                    {
                        hasDescendantInSet = true;
                        break;
                    }
                    // Walk up the parent chain using LoadNode for proper provider resolution
                    var parentNode = qec?.LoadNode(parentId.Value) ?? context.NodeStore?.GetNode(parentId.Value);
                    parentId = (parentNode as XdmNode)?.Parent;
                }
                if (hasDescendantInSet) break;
            }
            if (!hasDescendantInSet)
                result.Add(node);
        }
        // Sort by document order (NodeId)
        result.Sort(XdmNode.CompareDocumentOrder);
        // Return as object?[] (sequence), not List<object?> (which means XDM array)
        return ValueTask.FromResult<object?>(result.Count == 0 ? null
            : result.Count == 1 ? (object?)result[0] : result.Cast<object?>().ToArray());
    }
}
