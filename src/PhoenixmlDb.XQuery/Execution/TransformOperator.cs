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
/// Transform copy-modify-return (functional update).
/// Deep-copies source nodes, applies the modify clause's PUL to the copies,
/// then evaluates and returns the return clause.
/// </summary>
public sealed class TransformOperator : PhysicalOperator
{
    public required IReadOnlyList<TransformCopyBindingOperator> CopyBindings { get; init; }
    public required PhysicalOperator ModifyExpr { get; init; }
    public required PhysicalOperator ReturnExpr { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Save the outer PUL and create a fresh one for the modify clause
        var outerPul = context.PendingUpdates;
        var modifyPul = new Ast.PendingUpdateList();
        context.PendingUpdates = modifyPul;

        var store = new InMemoryUpdatableNodeStore();
        context.PushScope();
        context.PushNodeProvider(store);

        try
        {
            // Phase 1: Evaluate copy bindings — deep-copy each source node
            foreach (var binding in CopyBindings)
            {
                object? sourceValue = null;
                await foreach (var item in binding.Expression.ExecuteAsync(context))
                {
                    sourceValue = item;
                    break;
                }

                if (sourceValue is not PhoenixmlDb.Xdm.Nodes.XdmNode sourceNode)
                    throw new XQueryRuntimeException("XUTY0013",
                        "copy binding: source must be a node.");

                // Deep-copy with node resolution from the query context
                var copiedNode = store.DeepCopy(sourceNode, id =>
                {
                    // Try the store first (for nodes already copied), then fall back to context
                    return store.GetNode(id) ?? context.LoadNode(id);
                });

                context.BindVariable(binding.Variable, copiedNode);
            }

            // Phase 2: Evaluate modify clause — collects PUL entries against the copies
            await foreach (var _ in ModifyExpr.ExecuteAsync(context))
            {
                // Discard any values; we only want the PUL side effects
            }

            // Phase 3: Apply the collected PUL to the in-memory store
            if (modifyPul.HasUpdates)
            {
                PendingUpdateApplicator.Apply(modifyPul, store);
            }

            // Phase 3.5: Register all deep-copied (and potentially mutated) nodes in the
            // outer document store so the serializer can resolve them.
            if (context.NodeStore is INodeBuilder outerBuilder)
            {
                foreach (var node in store.AllNodes)
                    outerBuilder.RegisterNode(node);
            }

            // Phase 4: Evaluate and yield the return clause
            await foreach (var item in ReturnExpr.ExecuteAsync(context))
            {
                yield return item;
            }
        }
        finally
        {
            context.PopNodeProvider();
            context.PopScope();
            context.PendingUpdates = outerPul;
        }
    }
}
