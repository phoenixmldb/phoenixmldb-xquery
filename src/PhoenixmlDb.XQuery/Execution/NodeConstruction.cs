namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// The node builder a node constructor needs: the query's node store, when it implements
/// <see cref="INodeBuilder"/>.
/// </summary>
internal static class NodeConstruction
{
    /// <summary>
    /// The query's node builder, or an <see cref="InvalidOperationException"/> that names the
    /// constructor and the host's store. Every node constructor asks here, so each one fails in
    /// the same way when the host gives no store that builds nodes.
    /// </summary>
    /// <remarks>
    /// A text, comment, processing-instruction, attribute or namespace constructor with no builder
    /// made a node with the id 0, which is <see cref="Core.NodeId.None"/>: every such node had the
    /// same id, and no store could find it. The element and document constructors already threw
    /// this exception.
    /// </remarks>
    /// <param name="context">The query's context.</param>
    /// <param name="constructor">The constructor, as the message starts: "A text constructor".</param>
    /// <param name="node">The kind of node it makes: "text node".</param>
    internal static INodeBuilder RequireBuilder(QueryExecutionContext context, string constructor, string node)
        => context.NodeStore as INodeBuilder
            ?? throw new InvalidOperationException(
                $"{constructor} requires a node store implementing INodeBuilder, but the "
                + $"host supplied {context.NodeStore?.GetType().Name ?? "no node store"}. "
                + $"Without one no {node} can be constructed.");
}
