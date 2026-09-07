using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:partition($input as item()*, $split as function(item()*, item()) as xs:boolean)
///     as array(item())*
/// </summary>
/// <remarks>
/// XPath 4.0 §fn:partition. <c>$split</c> takes TWO arguments — the partition accumulated so
/// far and the next item — and returns true when that partition is COMPLETE, so the next item
/// begins a new one. The result is a sequence of arrays.
///
/// This was previously implemented as "split into runs where a one-argument predicate has the
/// same value", which is a different function. Martin Honnen's report (2026-08-22):
///
///     partition(1 to 7, function($partition, $next) { count($partition) eq 2 })
///
/// should give [1,2] [3,4] [5,6] [7] — four arrays — because the split closes a partition once
/// it holds two items. Calling the predicate with a single argument meant $partition was bound
/// to one item and count() was never 2, so nothing ever split and the whole input came back as
/// one group: `=> count()` returned 1 instead of 4.
/// </remarks>
public sealed class PartitionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "partition");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var split = arguments[1] as XQueryFunction;
        if (seq == null || split == null) return Array.Empty<object>();

        var items = seq is object?[] arr ? arr : new[] { seq };
        if (items.Length == 0) return Array.Empty<object>();

        var result = new List<object?>();
        // An ARRAY is List<object?>; a SEQUENCE is object?[]. The partitions are arrays, and
        // the returned sequence of them is an object?[].
        var current = new List<object?>();

        foreach (var item in items)
        {
            // $split sees the partition SO FAR and the item about to be added. True means that
            // partition is finished and this item opens the next one. The first call therefore
            // gets the empty sequence, which is what lets a predicate like `count($p) eq 2`
            // accumulate before it fires.
            if (current.Count > 0)
            {
                var verdict = await split
                    .InvokeAsync([current.ToArray(), item], context).ConfigureAwait(false);
                if (verdict is true)
                {
                    result.Add(current);
                    current = [];
                }
            }
            current.Add(item);
        }
        if (current.Count > 0)
            result.Add(current);

        return result.ToArray();
    }
}
