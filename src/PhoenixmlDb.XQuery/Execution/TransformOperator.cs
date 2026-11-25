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
        // The modify clause collects its updates in a list of its own, which is applied to the
        // copies before the return clause runs. The copy and return clauses add to the list that
        // is active outside (XQUF 3.0 §5.6: the expression's pending update list merges theirs),
        // and the return clause yields its items with that list active. The modify list stayed
        // active until the end, so a host that checks the list before each item saw updates the
        // query had not made (phx:XUDY9001 in a database), an update in the return clause was
        // added to a list that had already been applied, and was lost, and one in the copy clause
        // was refused with XUDY0014.
        var outerPul = context.PendingUpdates;
        var modifyPul = new Ast.PendingUpdateList();

        var store = new InMemoryUpdatableNodeStore();
        context.PushScope();
        context.PushNodeProvider(store);

        var copies = new List<(PhoenixmlDb.Core.QName Variable, PhoenixmlDb.Core.NodeId Id)>(CopyBindings.Count);
        try
        {
            // Phase 1: Evaluate copy bindings — deep-copy each source node
            foreach (var binding in CopyBindings)
            {
                // XQUF 3.0 §5.6: the source is a single node (XUTY0013); only its first item was
                // looked at.
                var sources = new List<object?>(2);
                await foreach (var item in binding.Expression.ExecuteGuarded(context))
                {
                    UpdateTargets.Flatten(item, sources);
                    if (sources.Count > 1)
                        break;
                }

                if (sources.Count != 1 || sources[0] is not PhoenixmlDb.Xdm.Nodes.XdmNode sourceNode)
                    throw new XQueryRuntimeException("XUTY0013",
                        "copy binding: the source must be a single node.");

                // A source of another store is first imported into the query's store: the overlay
                // copy keeps the source's namespace ids, which mean other URIs here (xslt BUGS.md #124).
                if (context.NodeStore is INodeBuilder queryStore)
                    sourceNode = ForeignTreeImporter.ImportIfForeign(sourceNode, queryStore);

                // Deep-copy with node resolution from the query context
                var copiedNode = store.DeepCopy(sourceNode, id =>
                {
                    // Try the store first (for nodes already copied), then fall back to context
                    return store.GetNode(id) ?? context.LoadNode(sourceNode, id);
                });

                context.BindVariable(binding.Variable, copiedNode);
                copies.Add((binding.Variable, copiedNode.Id));
            }

            // Phase 2: Evaluate modify clause — collects PUL entries against the copies
            // and copies the content of its updates into the copies' store.
            var outerModifyStore = context.ModifyClauseStore;
            context.PendingUpdates = modifyPul;
            context.ModifyClauseStore = store;
            try
            {
                await foreach (var _ in ModifyExpr.ExecuteGuarded(context))
                {
                    // Discard any values; we only want the PUL side effects
                }
            }
            finally
            {
                context.PendingUpdates = outerPul;
                context.ModifyClauseStore = outerModifyStore;
            }

            // Phase 3: Apply the collected PUL to the in-memory store
            if (modifyPul.HasUpdates)
            {
                // The modify clause may change only the nodes the copy clause made (XUDY0014). A
                // change of another node was ignored, silently (BUGS.md #128). A copy is the object
                // this store holds under its id.
                foreach (var primitive in modifyPul.Primitives)
                {
                    if (primitive.Target is PhoenixmlDb.Xdm.Nodes.XdmNode target && !ReferenceEquals(store.GetNode(target.Id), target))
                        throw new XQueryRuntimeException("XUDY0014",
                            $"The modify clause changes a {target.NodeKind} node that its copy clause did not create");
                }
                PendingUpdateApplicator.RequireCompatible(modifyPul);
                PendingUpdateApplicator.Apply(modifyPul, store);

                // An update can replace a node object (rename and replace value make a new object
                // with the same id). Children are found by id and see it; the copy variables hold
                // the root object itself, so rebind them to the current one: `rename node $c as
                // "x"` used to return the old name (BUGS.md #129).
                foreach (var (variable, id) in copies)
                {
                    if (store.GetNode(id) is { } current)
                        context.BindVariable(variable, current);
                }
            }

            // Phase 3.5: Register all deep-copied (and potentially mutated) nodes in the
            // outer document store so the serializer can resolve them.
            if (context.NodeStore is INodeBuilder outerBuilder)
            {
                foreach (var node in store.AllNodes)
                    outerBuilder.RegisterNode(node);
            }

            // Phase 4: Evaluate and yield the return clause
            await foreach (var item in ReturnExpr.ExecuteGuarded(context))
            {
                yield return item;
            }
        }
        finally
        {
            context.PopNodeProvider();
            context.PopScope();
        }
    }
}
