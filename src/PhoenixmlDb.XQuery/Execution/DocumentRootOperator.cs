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
/// Returns the document root for the context item's tree, or enumerates documents in a container.
/// </summary>
public sealed class DocumentRootOperator : PhysicalOperator
{
    public required ContainerId Container { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;

        // XPath semantics: "/" means the root of the context item's tree
        var contextItem = context.ContextItem;
        if (contextItem is XdmDocument doc)
        {
            yield return doc;
            yield break;
        }

        if (contextItem is XdmNode node)
        {
            // Navigate up to find the document root
            var current = node;
            while (current.Parent != NodeId.None)
            {
                var parent = context.LoadNode(current.Parent);
                if (parent == null)
                    break;
                current = parent;
            }
            // XPDY0050: If the root of the tree is not a document node, "/" is an error
            if (current is not XdmDocument)
                throw new XQueryRuntimeException("XPDY0050", "The context item for '/' is not in a tree rooted at a document node");
            yield return current;
            yield break;
        }

        // Context item is not a node — raise XPTY0020
        if (contextItem != null)
            throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0020",
                $"An axis step was used when the context item is not a node (got {DescribeItemType(contextItem)})",
                Location);
        // Per XPath 3.1 §3.3.2: a leading "/" requires a context item rooted at a document.
        // If the focus is absent, raise XPDY0002 — the same behavior as `.` with no focus.
        // Previously fell through silently, returning the empty sequence; this satisfied
        // QT3 K2-Axes-45's `(/, 1)[2]` only by accident (returning empty instead of "1"
        // or the spec-allowed XPDY0002).
        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPDY0002",
            "The context item is absent for '/' (initial root step requires a context item)",
            Location);
    }
}
