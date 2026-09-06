using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:transitive-closure($seq as item()*, $fn as function(item()) as item()*)  as item()*
/// Computes the transitive closure of a function over a sequence (XPath 4.0).
/// </summary>
public sealed class TransitiveClosureFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "transitive-closure");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "fn"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var fn = arguments[1] as XQueryFunction;
        if (seq == null || fn == null) return Array.Empty<object>();

        var items = seq is object?[] arr ? arr.ToList() : new List<object?> { seq };
        var result = new List<object?>(items);
        var seen = new HashSet<string>(items.Select(i => QueryExecutionContext.Atomize(i)?.ToString() ?? ""));
        var queue = new Queue<object?>(items);

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            var next = await fn.InvokeAsync([item], context).ConfigureAwait(false);
            var nextItems = next is object?[] na ? na : (next != null ? new[] { next } : Array.Empty<object?>());
            foreach (var ni in nextItems)
            {
                var key = QueryExecutionContext.Atomize(ni)?.ToString() ?? "";
                if (seen.Add(key))
                {
                    result.Add(ni);
                    queue.Enqueue(ni);
                }
            }
        }
        return result.ToArray();
    }
}
